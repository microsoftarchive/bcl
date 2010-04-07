// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Diagnostics.Eventing;
using System.Text.RegularExpressions;

/// <summary>
/// This is just a class that holds data.  It does nothing except support an 'update' events 
/// </summary>
public class FilterParams : INotifyPropertyChanged
{
    public FilterParams()
    {
        m_StartTimeRelMSec = "";
        m_EndTimeRelMSec = "";
        m_IncludeRegExs = "";
        m_ExcludeRegExs = "";
        m_FoldRegExs = "";
        m_GroupRegExs = "";
    }
    public FilterParams(FilterParams other)
    {
        m_StartTimeRelMSec = other.m_StartTimeRelMSec;
        m_EndTimeRelMSec = other.m_EndTimeRelMSec;
        m_IncludeRegExs = other.m_IncludeRegExs;
        m_ExcludeRegExs = other.m_ExcludeRegExs;
        m_FoldRegExs = other.m_FoldRegExs;
        m_GroupRegExs = other.m_GroupRegExs;
    }

    // TODO reject invalid input
    /// <summary>
    /// Events strictly less than this time are excluded.  
    /// </summary>
    public string StartTimeRelMSec
    {
        get { return m_StartTimeRelMSec; }
        set
        {
            if (value == m_StartTimeRelMSec) return; m_StartTimeRelMSec = ToDouble(value);
            SignalPropertyChange("StartTimeRelMSec");
        }
    }
    /// <summary>
    /// Events strictly greater than this time are excluded. 
    /// </summary>
    public string EndTimeRelMSec
    {
        get { return m_EndTimeRelMSec; }
        set
        {
            if (value == m_EndTimeRelMSec)
                return; m_EndTimeRelMSec = ToDouble(value);
            SignalPropertyChange("EndTimeRelMSec");
        }
    }
    // This allows both to be update.   TODO do this better ..
    public void SetRange(string start, string end)
    {
        m_StartTimeRelMSec = start;
        m_EndTimeRelMSec = end;

        var propertyChanged = PropertyChanged;
        if (propertyChanged != null)
        {
            propertyChanged(this, new PropertyChangedEventArgs("StartTimeRelMSec"));
            propertyChanged(this, new PropertyChangedEventArgs("EndTimeRelMSec"));
        }
        var changed = Changed;
        if (changed != null)
            changed();
    }
    /// <summary>
    /// Each stack must have at least one treeNode that matches one of the include patterns. 
    /// This allows you to focus on one particular function
    /// </summary>
    public string IncludeRegExs { get { return m_IncludeRegExs; } set { if (value == m_IncludeRegExs) return; m_IncludeRegExs = value; SignalPropertyChange("IncludeRegExs"); } }
    /// <summary>
    /// If any stack frame matches this regular expression it is removed from consideration.  
    /// This simulates what execution time would be if certain functions were very fast 
    /// (took zero time).  
    /// </summary>
    public string ExcludeRegExs { get { return m_ExcludeRegExs; } set { if (value == m_ExcludeRegExs) return; m_ExcludeRegExs = value; SignalPropertyChange("ExcludeRegExs"); } }
    /// <summary>
    /// Any sample that has a frame that matches this Regular expression will have its
    /// time folded into its parent (it is like this function and all its children were
    /// inlined).  
    /// </summary>
    public string FoldRegExs { get { return m_FoldRegExs; } set { if (value == m_FoldRegExs) return; m_FoldRegExs = value; SignalPropertyChange("FoldRegExs"); } }
    /// <summary>
    /// Strings of the form RegularExpression->Replace, where RegularExpression is any .NET Regular expression
    /// and 'Replace' is set of alphabetic characters or $ followed by digits which stand for the N'th capture
    /// from the pattern (see Substitions in .NET Regular expression
    /// </summary>
    public string GroupRegExs { get { return m_GroupRegExs; } set { if (value == m_GroupRegExs) return; m_GroupRegExs = value; SignalPropertyChange("GroupRegExs"); } }
    /// <summary>
    /// Event is fired when any propertyName is updated.  
    /// </summary>
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action Changed;
    #region private
    private void SignalPropertyChange(string propertyName)
    {
        var propertyChanged = PropertyChanged;
        if (propertyChanged != null)
            propertyChanged(this, new PropertyChangedEventArgs(propertyName));
        var changed = Changed;
        if (changed != null)
            changed();
    }
    private string ToDouble(string value)
    {
        double result;
        if (double.TryParse(value, out result))
            return value;
        return "";
    }

    string m_StartTimeRelMSec;
    string m_EndTimeRelMSec;
    string m_IncludeRegExs;
    string m_ExcludeRegExs;
    string m_FoldRegExs;
    string m_GroupRegExs;
    #endregion
}

/// <summary>
/// A FilterStackSource class knows how to group methods by using the regular expressions specified
/// in 'filterparms' (which is just a collection of user-specified strings).    
/// </summary>
public class FilterStackSource : StackSource
{
    public FilterStackSource(FilterParams filterParams, StackSource baseStackSource)
    {
        m_baseStackSource = baseStackSource;
        bool mayHaveFilterSet = filterParams.IncludeRegExs.Contains("Thread ") || filterParams.IncludeRegExs.Contains("Process ") ||
                                filterParams.ExcludeRegExs.Contains("Thread ") || filterParams.ExcludeRegExs.Contains("Process ");
        if (mayHaveFilterSet)
            m_ThreadFilterSet = new Dictionary<int, bool>();

        if (double.TryParse(filterParams.StartTimeRelMSec, out m_minTimeRelMSec))
            m_timeFilterActive = true;

        m_maxTimeRelMSec = double.PositiveInfinity;
        if (double.TryParse(filterParams.EndTimeRelMSec, out m_maxTimeRelMSec))
            m_timeFilterActive = true;

        m_includePats = ParseRegExList(filterParams.IncludeRegExs);
        m_inclPatsStillToBeMatched = new bool[m_includePats.Length];
        m_excludePats = ParseRegExList(filterParams.ExcludeRegExs);
        m_foldPats = ParseRegExList(filterParams.FoldRegExs);

        // parse the strings in the group specification 
        var groupsStr = filterParams.GroupRegExs.Trim();
        if (groupsStr.Length != 0)
        {
            var stringGroups = groupsStr.Split(';');
            var groups = new GroupPattern[stringGroups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                var stringGroup = stringGroups[i].Trim();
                if (stringGroup.Length == 0)
                    continue;

                var op = "=>";              // This means that you distinguish the entry points into the group
                int arrowIdx = stringGroup.IndexOf("=>");
                if (arrowIdx < 0)
                {
                    op = "->";              // This means you just group them losing information about what function was used to enter the group. 
                    arrowIdx = stringGroup.IndexOf("->");
                }
                var replaceStr = "$&";         // By default whatever we match is what we used as the replacement. 
                string patStr = null;
                if (arrowIdx >= 0)
                {
                    patStr = stringGroup.Substring(0, arrowIdx);
                    arrowIdx += 2;
                    replaceStr = stringGroup.Substring(arrowIdx, stringGroup.Length - arrowIdx).Trim();
                }
                else
                    patStr = stringGroup;
                var pat = new Regex(ToDotNetRegEx(patStr), RegexOptions.IgnoreCase | RegexOptions.Compiled);
                groups[i] = new GroupPattern(pat, replaceStr, op);
            }
            m_groups = groups;
        }
        else
            m_groups = new GroupPattern[0];

        if (m_groups.Length > 0 || m_includePats.Length > 0 || m_excludePats.Length > 0 || m_foldPats.Length > 0)
        {
            m_frameIdToFrameInfo = new FrameInfo[m_baseStackSource.MaxCallFrameIndex];
            for (int i = 0; i < m_frameIdToFrameInfo.Length; i++)
                m_frameIdToFrameInfo[i].FrameIndexForGroup = StackSourceFrameIndex.Invalid;

            if (m_groups.Length > 0)
                m_GroupNameToFrameInfoIndex = new Dictionary<string, StackSourceFrameIndex>();
        }
    }

    public override StackSourceSample GetNextSample()
    {
        var ret = m_baseStackSource.GetNextSample();
        if (ret == null)
            return null;

        if (m_timeFilterActive)
        {
            if (ret.TimeRelMSec < m_minTimeRelMSec || ret.TimeRelMSec > m_maxTimeRelMSec)
            {
                ret.Stack = StackSourceCallStackIndex.Discard;
                return ret;
            }
        }

        if (ret.ProcessID < 0)
        {
            ret.Stack = StackSourceCallStackIndex.Invalid;
            return ret;
        }

        // See if we can trim quickly based on thread or process. It is an optimization. 
        if (m_ThreadFilterSet != null && ret.ThreadID >= 0)
        {
            bool shouldInclude;
            if (!m_ThreadFilterSet.TryGetValue(ret.ThreadID, out shouldInclude))
                m_ThreadFilterSet[ret.ThreadID] = shouldInclude = ShouldIncludeThreadInSamples(ret);
            if (!shouldInclude)
            {
                ret.Stack = StackSourceCallStackIndex.Discard;
                return ret;
            }
        }

        Debug.Assert(ret.Stack != StackSourceCallStackIndex.Invalid);     // We always have at least the thread and process. 

        Debug.Assert(m_inclPatsStillToBeMatched.Length == m_includePats.Length);
        if (m_inclPatsStillToBeMatched.Length > 0)
        {
            // If the stack is empty, then we don't match 
            if (ret.Stack == StackSourceCallStackIndex.Invalid)
                ret.Stack = StackSourceCallStackIndex.Discard;

            // Reset the 'stillToBeMatched' information.  
            m_inclPatsStillToBeMatchedCount = m_inclPatsStillToBeMatched.Length;
            for (int i = 0; i < m_inclPatsStillToBeMatched.Length; i++)
                m_inclPatsStillToBeMatched[i] = true;
        }
        return ret;
    }
    public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
    {
        var ret = m_baseStackSource.GetCallerIndex(callStackIndex);
        // If we hit the top of the stack, we have an include pattern, and we have not
        // seen it, then discard the stack.  
        if (ret == StackSourceCallStackIndex.Invalid)
        {
            if (m_inclPatsStillToBeMatchedCount > 0)
                ret = StackSourceCallStackIndex.Discard;
        }
        return ret;
    }
    public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
    {
        // Find out what our frame index is before we apply grouping, folding and filtering...
        int rawFrameIndex = (int)m_baseStackSource.GetFrameIndex(callStackIndex);
        if (m_frameIdToFrameInfo == null || rawFrameIndex < 0)
            return (StackSourceFrameIndex)rawFrameIndex;

        // See if we have a cached answer (we hope we hit mos of the time
        StackSourceFrameIndex frameIndexRet = m_frameIdToFrameInfo[(int)rawFrameIndex].FrameIndexForGroup;
        if (frameIndexRet == StackSourceFrameIndex.Invalid)
        {
            // No hit.  First get the FULL name
            string fullFrameName = m_baseStackSource.GetFrameName((StackSourceFrameIndex)rawFrameIndex, true);
            string frameName = Regex.Replace(fullFrameName, @"^[^!]*\\", "");

            Debug.Assert(frameName != null);

            // Then find the group 
            bool isInternalGrouping;
            var groupName = FindGroupNameFromFrameName(fullFrameName, out isInternalGrouping);

            // Initialize the FrameInfo structure and set the frameIndexRet and the frameName variables 
            if (groupName != null)
            {
                // Now that we have the name, find the canonical frame that we use to represent the group as a whole
                StackSourceFrameIndex canonicalFrameIndexForGroup;
                if (m_GroupNameToFrameInfoIndex.TryGetValue(groupName, out canonicalFrameIndexForGroup))
                    frameIndexRet = canonicalFrameIndexForGroup;
                else
                {
                    // Don't have one.  the current frame becomes our cannonical one, and we create the rest of the group for it here.   
                    Debug.Assert(m_frameIdToFrameInfo[rawFrameIndex].GroupName == null);
                    Debug.Assert(m_frameIdToFrameInfo[rawFrameIndex].GroupId == 0);
                    // We use the ID of the first frame we encouter as the ID for the group as a whole.  
                    frameIndexRet = (StackSourceFrameIndex)rawFrameIndex;
                    m_frameIdToFrameInfo[rawFrameIndex].GroupName = groupName;
                    m_frameIdToFrameInfo[rawFrameIndex].GroupId = (StackSourceGroupIndex)m_GroupNameToFrameInfoIndex.Count;
                    m_GroupNameToFrameInfoIndex.Add(groupName, frameIndexRet);
                }
                if (isInternalGrouping)
                {
                    frameIndexRet = StackSourceFrameIndex.GroupInternal;
                    m_frameIdToFrameInfo[rawFrameIndex].GroupName = groupName + " <<" + frameName + ">>";
                }
                fullFrameName = m_frameIdToFrameInfo[rawFrameIndex].GroupName;
            }
            else
            {
                // Nothing matches, then do no morphing, and simply use the frame and frameName unchanged.  
                frameIndexRet = (StackSourceFrameIndex)rawFrameIndex;
                m_frameIdToFrameInfo[rawFrameIndex].GroupName = frameName;
            }
            // At this point frameNameIndex and frameName is set  

            // See if we should filter or fold it. 
            if (IsMatch(m_excludePats, fullFrameName) >= 0)
                frameIndexRet = StackSourceFrameIndex.Discard;
            else if (IsMatch(m_foldPats, fullFrameName) >= 0)
                frameIndexRet = StackSourceFrameIndex.Fold;

            m_frameIdToFrameInfo[rawFrameIndex].IncPatternsMatched = MatchSet(m_includePats, fullFrameName);
            m_frameIdToFrameInfo[rawFrameIndex].FrameIndexForGroup = frameIndexRet;
        }

        // If ther are any include patterns that have not yet been matched, then see if our
        // current frame method matches it. 
        if (m_inclPatsStillToBeMatchedCount > 0)
        {
            var incPatternsMatched = m_frameIdToFrameInfo[rawFrameIndex].IncPatternsMatched;
            if (incPatternsMatched != null)
            {
                // OK this frame matches some number of include patterns (typically 1).   Set
                // the cooresponding bit in the m_inclPatsStillToBeMatched vector and decrement
                // the count if we went from unmatched to matched
                foreach (int idx in incPatternsMatched)
                {
                    if (m_inclPatsStillToBeMatched[idx])
                    {
                        m_inclPatsStillToBeMatched[idx] = false;
                        --m_inclPatsStillToBeMatchedCount;
                        Debug.Assert(m_inclPatsStillToBeMatchedCount >= 0);
                    }
                }
            }
        }
        return frameIndexRet;
    }
    public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullName)
    {
        if (m_frameIdToFrameInfo != null && frameIndex >= 0)
            return m_frameIdToFrameInfo[(int)frameIndex].GroupName;
        else
            return m_baseStackSource.GetFrameName(frameIndex, fullName);
    }
    public override void GetGroupInternalInfo(StackSourceCallStackIndex callStackIndex, out StackSourceFrameIndex frameIndex, out StackSourceGroupIndex internalGroupIndex)
    {
        frameIndex = m_baseStackSource.GetFrameIndex(callStackIndex);
        FrameInfo info = m_frameIdToFrameInfo[(int)frameIndex];
        Debug.Assert(info.FrameIndexForGroup == StackSourceFrameIndex.GroupInternal || info.FrameIndexForGroup == StackSourceFrameIndex.GroupInternalAll);
        internalGroupIndex = info.GroupId;
    }
    public override int MaxCallStackIndex { get { return m_baseStackSource.MaxCallStackIndex; } }
    public override int MaxCallFrameIndex { get { return m_baseStackSource.MaxCallFrameIndex; } }

    public static string EscapeRegEx(string str)
    {
        // Right now I don't allow matching names with * in them, which is our wildcard
        return str;
    }
    #region private
    /// <summary>
    /// Holds parsed information about patterns for groups includes, excludes or folds.  
    /// </summary>
    private Regex[] ParseRegExList(string patterns)
    {
        patterns = patterns.Trim();
        if (patterns.Length == 0)
            return new Regex[0];
        var stringGroupPats = patterns.Split(';');
        var ret = new Regex[stringGroupPats.Length];
        for (int i = 0; i < ret.Length; i++)
        {
            var patStr = stringGroupPats[i].Trim();
            if (patStr.Length > 0)         // Skip empty entries.  
                ret[i] = new Regex(ToDotNetRegEx(patStr), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
        return ret;
    }
    private int IsMatch(Regex[] pats, string str)
    {
        for (int i = 0; i < pats.Length; i++)
        {
            var pat = pats[i];
            if (pat != null && pat.IsMatch(str))
                return i;
        }
        return -1;
    }
    // Returns the set of indexes for each pattern that matches.  
    private int[] MatchSet(Regex[] pats, string str)
    {
        int[] ret = null;
        int retCount = 0;
        for (int i = 0; i < pats.Length; i++)
        {
            var pat = pats[i];
            if (pat != null && pat.IsMatch(str))
            {
                // Note we can allocate a lot of arrays this way, but it likelyhood of matching
                // more than once is very low, so this is OK. 
                var newRet = new int[retCount + 1];
                if (retCount > 0)
                    Array.Copy(ret, newRet, retCount);
                newRet[retCount] = i;
                ret = newRet;
                retCount++;
            }
        }
        return ret;
    }

    private string FindGroupNameFromFrameName(string frameName, out bool isInternal)
    {
        // Look in every declared group looking for a match.  
        for (int i = 0; i < m_groups.Length; i++)
        {
            var candidateGroup = m_groups[i];
            if (candidateGroup == null)
                continue;
            var match = candidateGroup.Pattern.Match(frameName);
            if (match.Success)
            {
                var groupName = candidateGroup.GroupNameTemplate;
                if (candidateGroup.HasReplacements)
                {
                    if (groupName == "$&")
                        groupName = match.Groups[0].Value;
                    else
                    {
                        // Replace the $1, $2, ... with the strings that were matched in the original regexp.  
                        groupName = Regex.Replace(candidateGroup.GroupNameTemplate, @"\$(\d)",
                            replaceMatch => match.Groups[replaceMatch.Groups[1].Value[0] - '0'].Value);
                        // Replace $& with the match replacement.  
                        groupName = Regex.Replace(groupName, @"\$&", match.Groups[0].Value);
                    }
                }
                isInternal = candidateGroup.IsInternal;
                return groupName;
            }
        }
        isInternal = false;
        return null;
    }
    /// <summary>
    /// Convert a string from my regular explression format (where you only have * and {  } as grouping operators
    /// and convert them to .NET regular expressions string
    /// </summary>
    private static string ToDotNetRegEx(string str)
    {
        str = Regex.Escape(str);             // Assume everything is ordinary
        str = str.Replace(@"%", @"\w*");     // % means any number of alpha-numeric chars. 
        str = str.Replace(@"\*", @".*");     // * means any number of any characters.  
        str = str.Replace(@"\{", "(");
        str = str.Replace("}", ")");
        return @"\b" + str;             // By default it is anchored at the boundary of a word.  
    }

    /// <summary>
    /// Returns whether 'threadID' should be included in the stackSource
    /// </summary>
    private bool ShouldIncludeThreadInSamples(StackSourceSample sample)
    {
        Debug.Assert(m_includePats.Length != 0 || m_excludePats.Length != 0);

        bool ret = true;
        foreach (var pat in m_includePats)
        {
            var patStr = pat.ToString();
            if (patStr.StartsWith("^Thread"))
                ret = pat.IsMatch("Thread (" + sample.ThreadID + ")");
            if (patStr.StartsWith("^Process"))
                ret = pat.IsMatch("Process " + sample.ProcessName + " (" + sample.ProcessID + ")");

            // Every include pattern must be matched, so if we get a non-match we return immediately. 
            if (!ret)
                return false;
        }

        foreach (var pat in m_excludePats)
        {
            var patStr = pat.ToString();
            if (patStr.StartsWith("^Thread"))
                ret = !pat.IsMatch("Thread (" + sample.ThreadID + ")");
            if (patStr.StartsWith("^Process"))
                ret = !pat.IsMatch("Process " + sample.ProcessName + " (" + sample.ProcessID + ")");
            if (!ret)
                return false;
        }
        return ret;
    }

    /// <summary>
    /// This is just the parsed form of a grouping specification Pat->Group  (it has a pattern regular 
    /// expression and a group name that can have replacements)  It is a trivial class
    /// </summary>
    class GroupPattern
    {
        // PatternInfo is basically a union, and these static functions create each of the kinds of union.  
        public GroupPattern(Regex pattern, string groupNameTemplate, string op)
        {
            Pattern = pattern;
            GroupNameTemplate = groupNameTemplate;
            IsInternal = (op == "=>");
        }
        public string GroupNameTemplate;
        public Regex Pattern;
        public bool IsInternal;
        public bool HasReplacements { get { return GroupNameTemplate.IndexOf('$') >= 0; } }
        public override string ToString()
        {
            return string.Format("{0}->{1}", Pattern, GroupNameTemplate);
        }
        #region private
        private GroupPattern() { }
        #endregion
    }

    // FrameInfo is all the information we need to associate with an ungrouped Frame ID (to figure out what group/pattern 
    struct FrameInfo
    {
        /// <summary>
        /// This is what we return to the Stack crawler, it encodes either that we should filter the sample,
        /// fold the frame, form a group, or the frameID that we have chosen to represent the group as a whole.  
        /// </summary>
        public StackSourceFrameIndex FrameIndexForGroup;
        public StackSourceGroupIndex GroupId;               // This is just a number unique to the group (it is isomorphic to the name) 
        public string GroupName;                            // The name of the group.
        public int[] IncPatternsMatched;                    // Each element in array is an index into m_includePats, which this frame matches.  
    }

#if DEBUG
    // string m_stack;     // Useful for debugging
#endif
    // These are the 'raw' patterns that are just parsed form of what the user specified in the TextBox
    GroupPattern[] m_groups;
    Regex[] m_includePats;
    Regex[] m_excludePats;
    Regex[] m_foldPats;

    StackSource m_baseStackSource;

    // To avoid alot of regular expression matching, we remember for a given frame ID the pattern it matched 
    // This allows us to avoid string matching on all but the first lookup of a given frame.  
    FrameInfo[] m_frameIdToFrameInfo;
    // Once we have applied the regular expression to a group, we have a string, we need to find the
    // 'canonical' FrameInfo associated with that name, this mapping does that.  
    Dictionary<string, StackSourceFrameIndex> m_GroupNameToFrameInfoIndex;

    // If non-null, indicates a set of threads that are in the 'include' list.  
    Dictionary<int, bool> m_ThreadFilterSet;

    // The list of patterns that still have to be matched to be included.  
    private bool[] m_inclPatsStillToBeMatched;      // Each bit is set if the cooresponding pattern in m_includePats still needs to be matched
    private int m_inclPatsStillToBeMatchedCount;    // The count of the number of set bits in m_inclPatsStillToBeMatched
    private double m_minTimeRelMSec;
    private double m_maxTimeRelMSec;
    private bool m_timeFilterActive;
    #endregion
}
