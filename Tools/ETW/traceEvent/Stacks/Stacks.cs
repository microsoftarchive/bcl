// Copyright (c) Microsoft Corporation.  All rights reserved
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;                        // For TextWriter.  

/// <summary>
/// An opaque handle that are 1-1 with a complete call stack
/// 
/// </summary>
public enum StackSourceCallStackIndex
{
    Broken = 0,            // A Pseduo-frame that says that the stack is likely to actually be the top of the stack.  
    Start = 1,             // The first real call stack index (after the pseudo-ones before this)

    Invalid = -1,          // Returned when there is no caller (top of stack)
    Discard = -2           // Used by GetCallerIndex and GetStack to indicate that the sample should be discarded.  
};

public enum StackSourceFrameIndex
{
    Broken = 0,            // Pseduo-frame that represents the caller of all broken stacks. 
    Unknown = 1,           // Unknown what to do (Must be before the 'special ones below'    // Non negative represents normal m_frames (e.g. names of methods)
    Start = 2,             // The first real call stack index (after the pseudo-ones before this)

    Invalid = -1,           // Should not happen (uninitialized)
    Discard = -2,           // Sample has been filtered out
    Fold = -3,              // This frame should be folded (inlined) into the caller
    FoldAll = -4,           // This frame and all its callees should be folded (inlined) into the caller
    GroupInternal = -5,     // This frame is normal however any callees in the same group should be folded (see GetGroupInternalInfo)
    GroupInternalAll = -6,  // This frame is normal, however all callees should be folded  (see GetGroupInternalInfo)
};

/// <summary>
/// StackSourceSample represents a single sample that has a stack.  StackSource.GetNextSample returns these.  
/// </summary>
public class StackSourceSample
{
    public StackSourceCallStackIndex Stack;
    public float Metric;
    public double TimeRelMSec;
    public int ProcessID;
    public int ThreadID;
    public string ProcessName;
}

public enum StackSourceGroupIndex { Invalid = -1 };

/// <summary>
/// It is the abstract contract for a sample.  All we need is the Metric and 
/// </summary>
public abstract class StackSource
{
    public abstract StackSourceSample GetNextSample();
    /// <summary>
    /// Given a call stack, return the call stack of the caller.   This function can return StackSourceCallStackIndex.Discard
    /// which means that this sample should be discarded.  
    /// </summary>
    public abstract StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex);
    /// <summary>
    /// For efficiency, m_frames are assumed have a integer ID instead of a string name that
    /// is unique to the frame.  Note that it is expected that GetFrameIndex(x) == GetFrameId(y) 
    /// then GetFrameName(x) == GetFrameName(y).   The converse does NOT have to be true (you 
    /// can reused the same name for distict m_frames, however this can be confusing to your
    /// users, so be careful.  
    /// </summary>
    public abstract StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex);
    /// <summary>
    /// It is often useful to create a group (like all methods in a DLL), and fold away all INTERNAL nodes 
    /// of that group but LEAVE the 'public' (boundary) method intact.   To support this 'GetFrameIndex'
    /// can return either GroupInternal or GroupInternalAll.  However the crawler still needs the original frame
    /// (in case it should be left unmodified), as well as the group (to tell if it should be folded away).
    /// This routine fetches this information.   If GetFrameIndex never returns those enumerations than this
    /// method need not be overriden.  
    /// </summary>
    public virtual void GetGroupInternalInfo(StackSourceCallStackIndex callStackIndex, out StackSourceFrameIndex frameIndex, out StackSourceGroupIndex foldInternalGroup)
    {
        throw new NotImplementedException();
    }
    public abstract string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath);
    // all StackSourceCallStackIndex are guarenteed to be less than this
    public abstract int MaxCallStackIndex { get; }
    // all StackSourceFrameIndex are guarenteed to be less than this
    public abstract int MaxCallFrameIndex { get; }
}

/// <summary>
/// SampleInfos of a set of stackSource by eventToStack.  This represents the entire call tree.   You create an empty one in using
/// the default constructor and use 'AddSample' to add stackSource to it.   You traverse it by 
/// </summary>
public class CallTree
{
    public CallTree()
    {
        m_top = new CallTreeNode("ROOT", StackSourceFrameIndex.Unknown, null, this);
    }

    // Create a calltree using 'stackSource' as the object that knows how to take call stack indexes 
    // and return useful information about them (their caller, and name)
    public CallTree(StackSource stackSource)
    {
        m_top = new CallTreeNode("ROOT", StackSourceFrameIndex.Unknown, null, this);
        m_SampleInfo = stackSource;
        // And the basis for forming the % is total number of stackSource.  
        PercentageBasis = Top;
        m_frames = new FrameInfo[100];

        for (; ; )
        {
            var sample = stackSource.GetNextSample();
            if (sample == null)
                break;
            AddSample(sample);
        }
        // By default sort by inclusive Metric
        SortInclusiveMetricDecending();

        FoldNodesUnderOrEqual(2);          // Remove single count nodes TODO this is a hack.

        m_frames = null;            // Frames not needed anymore.  
    }
    public CallTreeNode Top { get { return m_top; } }
    public CallTreeNodeBase PercentageBasis { get; set; }

    /// <summary>
    /// Cause each treeNode in the calltree to be sorted (accending) based on comparer
    /// </summary>
    public void Sort(Comparison<CallTreeNode> comparer)
    {
        m_top.SortAll(comparer);
    }
    /// <summary>
    /// Sorting by InclusiveMetric Decending is so common, provide a shortcut.  
    /// </summary>
    public void SortInclusiveMetricDecending()
    {
        Sort(delegate(CallTreeNode x, CallTreeNode y)
        {
            int ret = y.InclusiveMetric.CompareTo(x.InclusiveMetric);
            if (ret != 0)
                return ret;
            // Sort by first sample time (assending) if the counts are the same.  
            return x.FirstTimeRelMSec.CompareTo(y.FirstTimeRelMSec);
        });
    }
    public void ToXml(TextWriter writer, float inclusiveMetricThreasholdPercent)
    {
        float absoluteThreashold = inclusiveMetricThreasholdPercent / 100.0F * Top.InclusiveMetric;

        ToXml(writer, true, "InclusivePercent GE " + inclusiveMetricThreasholdPercent.ToString("f1"), delegate(CallTreeNode node)
        {
            return (node.InclusiveMetric >= absoluteThreashold);
        });
    }
    public void ToXml(TextWriter writer, bool sumarizeFilteredNodes, string filterName, Predicate<CallTreeNode> filter)
    {
        if (filterName != null)
            writer.WriteLine("<CallTree FilterName=\"{0}\">", filterName);
        else
            writer.WriteLine("<CallTree>");
        Top.ToXml(writer, sumarizeFilteredNodes, "", filter);
        writer.WriteLine("</CallTree>");
    }
    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        ToXml(sw, false, null, null);
        return sw.ToString();
    }
    // Get a callerSum-calleeSum treeNode for 'nodeName'
    public CallerCalleeNode CallerCallee(string nodeName)
    {
        return new CallerCalleeNode(nodeName, this);
    }
    /// <summary>
    /// Return a list of nodes that have statisicts rolled up by treeNode (eg method) name.  It is not
    /// sorted by anything in particular.  
    /// </summary>
    public IEnumerable<CallTreeNodeBase> ByName { get { return GetSumByID().Values; } }
    public List<CallTreeNodeBase> ByNameSortedExclusiveMetric()
    {
        var ret = new List<CallTreeNodeBase>(ByName);
        // TODO HACK
#if false 
        foreach (var treeNode in ByName)
        {
            if (treeNode.ExclusiveCount == treeNode.InclusiveCount || treeNode.ExclusiveCount >= 10)
                ret.Add(treeNode);
        }
#endif
        ret.Sort((x, y) => y.ExclusiveMetric.CompareTo(x.ExclusiveMetric));
        return ret;
    }
    public List<CallTreeNodeBase> ByNameSortedInclusiveMetric()
    {
        var ret = new List<CallTreeNodeBase>(ByName);
        ret.Sort((x, y) => y.InclusiveMetric.CompareTo(x.InclusiveMetric));
        return ret;
    }

    /// <summary>
    /// If there are any nodes that have less than (or equal) to 'minInclusiveMetric'
    /// then remove the node, placing its samples into its parent (thus the parent's
    /// exclusive metric goes up)
    /// </summary>
    public int FoldNodesUnderOrEqual(float minInclusiveMetric)
    {
        int ret = m_top.FoldNodesUnder(minInclusiveMetric);
        m_sumByID = null;   // Force a recalculation of the list by ID
        return ret;
    }

    #region private
    private struct FrameInfo
    {
        public StackSourceFrameIndex frameIndex;
        public int numFolds;
    }

    private void AddSample(StackSourceSample sample)
    {
        // A 'fast' stack
        bool foldRecursion = true;

        // form the list of m_frames (we need it in reverse order), filtering or grouping as we go.  
        int framePtr = 0;
        int numberOfFoldsForCallerNode = 0;
        StackSourceGroupIndex lastGroupInternal = StackSourceGroupIndex.Invalid;
        StackSourceFrameIndex lastFrameIndex = StackSourceFrameIndex.Invalid;
        int prevGroupInternalFramePtr = 0;
        int frameCount = 0;

        var callStackIndex = sample.Stack;
        while (callStackIndex != StackSourceCallStackIndex.Invalid)
        {
            if (callStackIndex == StackSourceCallStackIndex.Discard)
                return;

            var frameIndex = m_SampleInfo.GetFrameIndex(callStackIndex);
            Debug.Assert(frameIndex != StackSourceFrameIndex.Invalid);
            frameCount++;
            if (frameIndex < 0)
            {
                if (frameIndex == StackSourceFrameIndex.Discard)
                {
                    // Debug.WriteLine("Frame Discard");
                    return;
                }
                else if (frameIndex == StackSourceFrameIndex.FoldAll)
                {
                    numberOfFoldsForCallerNode = frameCount;
                    framePtr = 0;
                    // Debug.WriteLine("Frame FoldAll");
                    goto NextStack;
                }
                else if (frameIndex == StackSourceFrameIndex.Fold)
                {
                    numberOfFoldsForCallerNode++;
                    // Debug.WriteLine("Frame Fold");
                    goto NextStack;
                }
                else if (frameIndex == StackSourceFrameIndex.GroupInternal ||
                         frameIndex == StackSourceFrameIndex.GroupInternalAll)
                {
                    if (frameIndex == StackSourceFrameIndex.GroupInternalAll)
                    {
                        // Debug.WriteLine("GroupAll");
                        numberOfFoldsForCallerNode = frameCount - 1;
                        framePtr = 0;
                    }

                    StackSourceGroupIndex groupInternal;
                    m_SampleInfo.GetGroupInternalInfo(callStackIndex, out frameIndex, out groupInternal);
#if DEBUG
                    // var name = m_SampleInfo.GetFrameName(frameIndex);
                    // Debug.WriteLine("Group or GroupAll Name = " + name + " groupID = " + groupInternal.ToString());
#endif

                    // Was the previous stack frame this same group?  
                    if (groupInternal == lastGroupInternal && framePtr == prevGroupInternalFramePtr + numberOfFoldsForCallerNode + 1)
                    {
                        // Fold this node into the last one
                        m_frames[prevGroupInternalFramePtr].frameIndex = frameIndex;
                        m_frames[prevGroupInternalFramePtr].numFolds += numberOfFoldsForCallerNode + 1;

                        numberOfFoldsForCallerNode = 0;
                        // Debug.WriteLine("GroupInternalFold");
                        goto NextStack;
                    }
                    else
                    {
                        // Go ahead an add the frame to the stack.   However remember enough so that if
                        // we hit this same group we will know to fold it.  
                        lastGroupInternal = groupInternal;
                        prevGroupInternalFramePtr = framePtr;
                        // Debug.WriteLine("GroupInternal");
                        goto AddStack;
                    }
                }
                else
                {
                    // Debug.WriteLine("Frame ERROR!");
                    Debug.Assert(false);
                    goto NextStack;
                }
            }
            else if ((foldRecursion && lastFrameIndex == frameIndex))
            {
                Debug.Assert(framePtr > 0 && m_frames[framePtr - 1].frameIndex == frameIndex);
                m_frames[framePtr - 1].numFolds += numberOfFoldsForCallerNode + 1;
                numberOfFoldsForCallerNode = 0;
                // Debug.WriteLine("Recursion Group");
                goto NextStack;
            }
        // Normal case, add to stack
        AddStack:
            // Debug.WriteLine("Stack " + framePtr + " Frame " + frameIndex + " = " + (frameIndex >= 0 ? m_SampleInfo.GetFrameName(frameIndex) : "?"));
            if (framePtr >= m_frames.Length)
            {
                var newFrames = new FrameInfo[m_frames.Length * 2];
                Array.Copy(m_frames, newFrames, m_frames.Length);
                m_frames = newFrames;
            }
            Debug.Assert(frameIndex >= 0);
            m_frames[framePtr].frameIndex = frameIndex;
            m_frames[framePtr].numFolds = numberOfFoldsForCallerNode;
            framePtr++;
            lastFrameIndex = frameIndex;
            numberOfFoldsForCallerNode = 0;
        NextStack:
            callStackIndex = m_SampleInfo.GetCallerIndex(callStackIndex);
        }

        // Debug.WriteLine("Adding Event at time " + event_.TimeStampRelativeMSec.ToString("f3"));

        // Now we are ready to add the sample to the tree, starting from the top node
        // and going down to the leaf
        var treeNode = m_top;
        // Debug.WriteLine("Adding to Tree framePtr = " + framePtr);
#if DEBUG
        int checkFrameCount = 0;
#endif
        for (; ; )
        {

            if (treeNode.MaxFoldedFrames < numberOfFoldsForCallerNode)
                treeNode.MaxFoldedFrames = numberOfFoldsForCallerNode;
            if (numberOfFoldsForCallerNode < treeNode.MinFoldedFrames)
                treeNode.MinFoldedFrames = numberOfFoldsForCallerNode;
#if DEBUG
            checkFrameCount += numberOfFoldsForCallerNode + 1;
            Debug.Assert(treeNode.Name != null);
            // Debug.WriteLine("Adding to Tree " + treeNode.Name);
#endif
            if (framePtr == 0)         // we are at a leaf method.  
            {
                //Debug.WriteLine("Adding to Tree " + treeNode.Name);
                treeNode.m_exclusiveCount++;
                treeNode.m_exclusiveMetric += sample.Metric;
            }
            treeNode.m_inclusiveCount++;
            treeNode.m_inclusiveMetric += sample.Metric;

            if (sample.TimeRelMSec < treeNode.m_firstTimeRelMSec)
                treeNode.m_firstTimeRelMSec = sample.TimeRelMSec;
            if (sample.TimeRelMSec > treeNode.m_lastTimeRelMSec)
                treeNode.m_lastTimeRelMSec = sample.TimeRelMSec;
            Debug.Assert(treeNode.m_firstTimeRelMSec <= treeNode.m_lastTimeRelMSec);

            if (framePtr <= 0)
                break;
            --framePtr;
            var frame = m_frames[framePtr].frameIndex;
            numberOfFoldsForCallerNode = m_frames[framePtr].numFolds;
            treeNode = treeNode.FindCallee(frame);
        }

#if DEBUG
        // + 1 is for the ROOT node.  
        Debug.Assert(frameCount + 1 == checkFrameCount);
        if (frameCount + 1 != checkFrameCount)
            Debugger.Break();
#endif

    }
    private Dictionary<int, CallTreeNodeBase> GetSumByID()
    {
        if (m_sumByID == null)
        {
            m_sumByID = new Dictionary<int, CallTreeNodeBase>();
            var callersOnStack = new Dictionary<int, CallTreeNodeBase>();       // This is just a set
            AccumulateSumByID(m_top, callersOnStack);
        }
        return m_sumByID;
    }
    /// <summary>
    /// Traverse the subtree of 'treeNode' into the m_sumByID dictionary.   We don't want to
    /// double-count inclusive times, so we have to keep track of all m_callers currently on the
    /// stack and we only add inclusive times for nodes that are not already on the stack.  
    /// </summary>
    private void AccumulateSumByID(CallTreeNode treeNode, Dictionary<int, CallTreeNodeBase> callersOnStack)
    {
        CallTreeNodeBase byIDNode;
        if (!m_sumByID.TryGetValue((int)treeNode.m_id, out byIDNode))
        {
            byIDNode = new CallTreeNodeBase(treeNode.Name, treeNode.m_id, this);
            m_sumByID.Add((int)treeNode.m_id, byIDNode);
        }

        bool newOnStack = !callersOnStack.ContainsKey((int)treeNode.m_id);
        // Add in the tree treeNode's contribution
        byIDNode.Combine(treeNode, newOnStack);
        if (treeNode.m_callees != null)
        {
            if (newOnStack)
                callersOnStack.Add((int)treeNode.m_id, null);
            foreach (var child in treeNode.m_callees)
                AccumulateSumByID(child, callersOnStack);
            if (newOnStack)
                callersOnStack.Remove((int)treeNode.m_id);
        }
    }

    internal StackSource m_SampleInfo;
    private CallTreeNode m_top;
    Dictionary<int, CallTreeNodeBase> m_sumByID;
    private FrameInfo[] m_frames;       // Used to invert the stack only used during 'AddSample' phase.  
    #endregion
}

/// <summary>
/// The part of a calltreeNode that is common to Caller-calleeSum and the calltree view.  
/// </summary>
public class CallTreeNodeBase
{
    public string Name { get { return m_name; } }
    /// <summary>
    /// Name also includes an indication of how many stack m_frames were folded into this 
    /// node (if you don't want this use Name)
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (MaxFoldedFrames == 0 || MinFoldedFrames == int.MaxValue)
                return Name;
            return Name + " [" + (MinFoldedFrames + 1) + "-" + (MaxFoldedFrames + 1) + " frames]";
        }
    }
    StackSourceFrameIndex ID { get { return m_id; } }
    public float InclusiveMetric { get { return m_inclusiveMetric; } }
    public float ExclusiveMetric { get { return m_exclusiveMetric; } }
    public float InclusiveCount { get { return m_inclusiveCount; } }
    public float ExclusiveCount { get { return m_exclusiveCount; } }
    public float InclusiveMetricPercent { get { return m_inclusiveMetric * 100 / m_container.PercentageBasis.InclusiveMetric; } }
    public float ExclusiveMetricPercent { get { return m_exclusiveMetric * 100 / m_container.PercentageBasis.InclusiveMetric; } }

    public double FirstTimeRelMSec { get { return m_firstTimeRelMSec; } }
    public double LastTimeRelMSec { get { return m_lastTimeRelMSec; } }
    public double DurationMSec { get { return m_lastTimeRelMSec - m_firstTimeRelMSec; } }
    /// <summary>
    /// If this node represents more than one call frame, this is the count of additional 
    /// m_frames folded into this node.  
    /// </summary>
    public int MaxFoldedFrames { get; internal set; }
    public int MinFoldedFrames { get; internal set; }

    public void ToXmlAttribs(TextWriter writer)
    {
        writer.Write(" Name=\"{0}\"", XmlUtilities.XmlEscape(Name, false));
        writer.Write(" InclusiveMetric=\"{0}\"", InclusiveMetric);
        writer.Write(" ExclusiveMetric=\"{0}\"", ExclusiveMetric);
        writer.Write(" InclusiveCount=\"{0}\"", InclusiveCount);
        writer.Write(" ExclusiveCount=\"{0}\"", ExclusiveCount);
        writer.Write(" FirstTimeRelMSec=\"{0:f4}\"", FirstTimeRelMSec);
        writer.Write(" LastTimeRelMSec=\"{0:f4}\"", LastTimeRelMSec);
    }
    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        sw.Write("<Node");
        ToXmlAttribs(sw);
        sw.Write("/>");
        return sw.ToString();
    }

    #region private
    internal CallTreeNodeBase(string name, StackSourceFrameIndex id, CallTree container)
    {
        this.m_name = name;
        this.m_container = container;
        this.m_id = id;
        this.m_firstTimeRelMSec = Double.PositiveInfinity;
        this.m_lastTimeRelMSec = Double.NegativeInfinity;
        this.MinFoldedFrames = int.MaxValue;
    }
    // TODO should this be public?
    public void Combine(CallTreeNodeBase other, bool addInclusive)
    {
        if (addInclusive)
        {
            m_inclusiveMetric += other.m_inclusiveMetric;
            m_inclusiveCount += other.m_inclusiveCount;
        }
        m_exclusiveMetric += other.m_exclusiveMetric;
        m_exclusiveCount += other.m_exclusiveCount;
        if (other.m_firstTimeRelMSec < m_firstTimeRelMSec)
            m_firstTimeRelMSec = other.m_firstTimeRelMSec;
        if (other.m_lastTimeRelMSec > m_lastTimeRelMSec)
            m_lastTimeRelMSec = other.m_lastTimeRelMSec;

        if (m_firstTimeRelMSec > m_lastTimeRelMSec)
            Debug.WriteLine("Error First > Last");
    }
    internal StackSourceFrameIndex m_id;
    internal readonly string m_name;
    internal readonly CallTree m_container;
    internal float m_inclusiveMetric;
    internal float m_exclusiveMetric;
    internal float m_inclusiveCount;
    internal float m_exclusiveCount;
    internal double m_firstTimeRelMSec;
    internal double m_lastTimeRelMSec;
    #endregion
}

/// <summary>
/// Represents a single treeNode in a code:CallTree 
/// 
/// TODO should a sort the Callees by name, or inclusive Metric?
/// </summary>
public sealed class CallTreeNode : CallTreeNodeBase
{
    public CallTreeNode Caller { get { return m_caller; } }
    public IList<CallTreeNode> Callees { get { return m_callees; } }
    public bool isLeaf { get { return m_callees == null; } }
    public void SortAll(Comparison<CallTreeNode> comparer)
    {
        if (m_callees != null)
        {
            m_callees.Sort(comparer);
            for (int i = 0; i < m_callees.Count; i++)
                m_callees[i].SortAll(comparer);
        }

    }

    public void ToXml(TextWriter writer, bool sumarizeFilteredNodes, string indent, Predicate<CallTreeNode> filter)
    {

        writer.Write("{0}<CallTree ", indent);
        this.ToXmlAttribs(writer);
        writer.WriteLine(">");

        var childIndent = indent + " ";
        if (m_callees != null)
        {
            CallTreeNode summaryNode = null;
            foreach (CallTreeNode callee in m_callees)
            {
                if (filter == null || filter(callee))
                {
                    callee.ToXml(writer, sumarizeFilteredNodes, childIndent, filter);
                }
                else if (sumarizeFilteredNodes)
                {
                    if (summaryNode == null)
                        summaryNode = new CallTreeNode("$FilteredNodes", 0, this, m_container);

                    summaryNode.m_exclusiveCount += callee.m_exclusiveCount;
                    summaryNode.m_exclusiveMetric += callee.m_exclusiveMetric;
                    summaryNode.m_inclusiveCount += callee.m_inclusiveCount;
                    summaryNode.m_inclusiveMetric += callee.m_inclusiveMetric;
                    if (callee.m_firstTimeRelMSec < summaryNode.m_firstTimeRelMSec)
                        summaryNode.m_firstTimeRelMSec = callee.m_firstTimeRelMSec;
                    if (callee.m_lastTimeRelMSec > summaryNode.m_lastTimeRelMSec)
                        summaryNode.m_lastTimeRelMSec = callee.m_lastTimeRelMSec;
                }
            }

            if (summaryNode != null)
                summaryNode.ToXml(writer, false, childIndent, filter);
        }
        writer.WriteLine("{0}</CallTree>", indent);
    }
    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        ToXml(sw, false, "", null);
        return sw.ToString();
    }
    #region private
    public CallTreeNode(string name, StackSourceFrameIndex id, CallTreeNode caller, CallTree container)
        : base(name, id, container)
    {
        this.m_caller = caller;
    }

    internal int FoldNodesUnder(float minInclusiveMetric)
    {
        int nodesFolded = 0;
        if (m_callees != null)
        {
            int to = 0;
            for (int from = 0; from < m_callees.Count; from++)
            {
                var callee = m_callees[from];
                if (callee.InclusiveCount <= minInclusiveMetric)
                {
                    nodesFolded++;
                    m_exclusiveCount += callee.m_inclusiveCount;
                    m_exclusiveMetric += callee.m_inclusiveMetric;
                }
                else
                {
                    nodesFolded += callee.FoldNodesUnder(minInclusiveMetric);
                    if (to != from)
                        m_callees[to] = m_callees[from];
                    to++;
                }
            }

            if (to == 0)
                m_callees = null;
            else if (to != m_callees.Count)
                m_callees.RemoveRange(to, m_callees.Count - to);
            Debug.Assert((to == 0  && m_callees == null) || to == m_callees.Count);
        }

        Debug.Assert(this == m_container.Top || InclusiveMetric >= minInclusiveMetric);
        Debug.Assert(InclusiveMetric >= ExclusiveMetric);
        Debug.Assert(m_callees != null || ExclusiveMetric == InclusiveMetric);
        return nodesFolded;
    }

    internal CallTreeNode FindCallee(StackSourceFrameIndex frameID)
    {
        CallTreeNode callee;
        if (m_callees != null)
        {
            for (int i = 0; i < m_callees.Count; i++)
            {
                callee = m_callees[i];
                if (callee.m_id == frameID)
                    return callee;
#if DEBUG
                if (callee.Name == m_container.m_SampleInfo.GetFrameName(frameID, false))
                    Debug.WriteLine(string.Format("Warning Got frame ID {0} Name {1} == {2} Name {3}", callee.m_id, callee.Name, frameID, m_container.m_SampleInfo.GetFrameName(frameID, false)));
#endif
            }
        }
        else
            m_callees = new List<CallTreeNode>();

        string frameName = m_container.m_SampleInfo.GetFrameName(frameID, false);
        Debug.Assert(frameName != null);

        callee = new CallTreeNode(frameName, frameID, this, m_container);
        m_callees.Add(callee);
        return callee;
    }

    // state;
    private readonly CallTreeNode m_caller;
    internal List<CallTreeNode> m_callees;
    #endregion
}

/// <summary>
/// A code:CallerCalleeNode gives statistics that focus on a particular location (methodIndex, module, or other
/// grouping).   It takes all stackSource that have callStacks that include that treeNode and compute the metrics for
/// all the m_callers and all the m_callees for that treeNode.  
/// </summary>
public class CallerCalleeNode : CallTreeNodeBase
{
    /// <summary>
    /// Given a complete call tree, and a Name within that call tree to focus on, create a
    /// CallerCalleeNode that represents the single Caller-Callee view for that treeNode. 
    /// </summary>
    public CallerCalleeNode(string nodeName, CallTree callTree)
        : base(nodeName, StackSourceFrameIndex.Invalid, callTree)
    {
        m_callees = new List<CallTreeNodeBase>();
        m_callers = new List<CallTreeNodeBase>();
        float totalMetric;
        float totalCount;
        AccumlateSamplesForNode(callTree.Top, 0, out totalMetric, out totalCount);
        Debug.Assert(totalCount <= callTree.Top.InclusiveCount);
        Debug.Assert(totalMetric <= callTree.Top.InclusiveMetric);

        m_callers.Sort((x, y) => y.InclusiveMetric.CompareTo(x.InclusiveMetric));
        m_callees.Sort((x, y) => y.InclusiveMetric.CompareTo(x.InclusiveMetric));

#if DEBUG
        float callerSum = 0;
        foreach (var caller in m_callers)
            callerSum += caller.m_inclusiveMetric;

        float calleeSum = 0;
        foreach (var callee in m_callees)
            calleeSum += callee.m_inclusiveMetric;

        if (this.Name != m_container.Top.Name)
            Debug.Assert(Math.Abs(callerSum - m_inclusiveMetric) < .01);
        Debug.Assert(Math.Abs(calleeSum + m_exclusiveMetric - m_inclusiveMetric) < .01);
#endif
    }

    public IEnumerable<CallTreeNodeBase> Callers { get { return m_callers; } }
    public IEnumerable<CallTreeNodeBase> Callees { get { return m_callees; } }

    public void ToXml(TextWriter writer, string indent)
    {
        writer.Write("{0}<CallerCallee", indent); this.ToXmlAttribs(writer); writer.WriteLine(">");
        writer.WriteLine("{0} <Callers Count=\"{1}\">", indent, m_callers.Count);
        foreach (CallTreeNodeBase caller in m_callers)
        {
            writer.Write("{0}  <Node", indent);
            caller.ToXmlAttribs(writer);
            writer.WriteLine("/>");
        }
        writer.WriteLine("{0} </Callers>", indent);
        writer.WriteLine("{0} <Callees Count=\"{1}\">", indent, m_callees.Count);
        foreach (CallTreeNodeBase callees in m_callees)
        {
            writer.Write("{0}  <Node", indent);
            callees.ToXmlAttribs(writer);
            writer.WriteLine("/>");
        }
        writer.WriteLine("{0} </Callees>", indent);
        writer.WriteLine("{0}</CallerCallee>", indent);
    }
    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        ToXml(sw, "");
        return sw.ToString();
    }
    #region private
    /// <summary>
    /// Accumlate all the stackSource represented by 'treeNode' and all its children into the current
    /// CallerCalleeNode represention for 'this.Name' (called the focus treeNode) 'recursionCount' is
    /// the number of times 'this.Name' has been seen as Caller on the path to the root (not
    /// including 'treeNode' itself). This method returns the inclusive Metric and the inclusive count
    /// for the calltree 'treeNode'. These inclusive metrics are weighted as described below.   
    /// 
    /// Recursive methods can easily cause double-counting in a Caller-Callee view for the
    /// inclusive times of the focus treeNode because one sample is counted in the inclusive time for
    /// every callee that contains a recusive path to the root. Thus one sample is being counted
    /// more than once.
    /// 
    /// To avoid this we 'split' the sample by the recursion count of focus method along every
    /// path.  These split stackSource are 'double counted' in the normal inclusive rollup, however
    /// because they were split they add up to 1 and thus the sum works out.  
    ///   
    /// There are other ways to solve this problem (only counting the topmost or bottom most frame
    /// in a recursive stack), however these skew the caller-callee stats more (either making it
    /// look like no recursive calls come or or go out).   Splitting seems to be the most 'fair'  
    /// (but does means that 'counts' can now be fractional). 
    /// </summary>
    private void AccumlateSamplesForNode(CallTreeNode treeNode, int recursionCount, out float inclusiveMetricRet, out float inclusiveCountRet)
    {
        inclusiveMetricRet = 0;
        inclusiveCountRet = 0;
        bool isFocusNode = treeNode.Name.Equals(Name);
        if (isFocusNode)
            recursionCount++;

        if (recursionCount > 0)
        {
            // Compute exclusive count and Metric (and initialize the inclusive count and Metric). 
            inclusiveCountRet = treeNode.ExclusiveMetric / recursionCount;
            inclusiveMetricRet = treeNode.ExclusiveMetric / recursionCount;
            if (isFocusNode)
            {
                m_exclusiveCount += inclusiveCountRet;
                m_exclusiveMetric += inclusiveMetricRet;
            }
        }

        // Get all the stackSource for the children and set the calleeSum information
        if (treeNode.m_callees != null)
        {
            for (int i = 0; i < treeNode.m_callees.Count; i++)
            {
                CallTreeNode calleeTreeNode = treeNode.m_callees[i];

                float calleeInclusiveMetric;
                float calleeInclusiveCount;
                AccumlateSamplesForNode(calleeTreeNode, recursionCount, out calleeInclusiveMetric, out calleeInclusiveCount);

                if (calleeInclusiveCount > 0)       // This condition is an optimization (avoid lookup when count zero)
                {
                    inclusiveCountRet += calleeInclusiveCount;
                    inclusiveMetricRet += calleeInclusiveMetric;
                    if (isFocusNode)
                    {
                        CallTreeNodeBase calleeSum = Find(ref m_callees, calleeTreeNode.Name);
                        calleeSum.m_inclusiveCount += calleeInclusiveCount;
                        calleeSum.m_inclusiveMetric += calleeInclusiveMetric;
                        calleeSum.m_exclusiveCount += calleeTreeNode.m_exclusiveCount;
                        calleeSum.m_exclusiveMetric += calleeTreeNode.m_exclusiveMetric;

                        if (calleeTreeNode.m_firstTimeRelMSec < calleeSum.m_firstTimeRelMSec)
                            calleeSum.m_firstTimeRelMSec = calleeTreeNode.m_firstTimeRelMSec;
                        if (calleeTreeNode.m_lastTimeRelMSec > calleeSum.m_lastTimeRelMSec)
                            calleeSum.m_lastTimeRelMSec = calleeTreeNode.m_lastTimeRelMSec;
                    }
                }
            }
        }

        // Set my nodes info
        if (isFocusNode)
        {
            m_inclusiveCount += inclusiveCountRet;
            m_inclusiveMetric += inclusiveMetricRet;

            if (treeNode.m_firstTimeRelMSec < m_firstTimeRelMSec)
                m_firstTimeRelMSec = treeNode.m_firstTimeRelMSec;
            if (treeNode.m_lastTimeRelMSec > m_lastTimeRelMSec)
                m_lastTimeRelMSec = treeNode.m_lastTimeRelMSec;

            // Set the Caller information now 
            CallTreeNode callerTreeNode = treeNode.Caller;
            if (callerTreeNode != null)
            {
                CallTreeNodeBase callerSum = Find(ref m_callers, callerTreeNode.Name);
                callerSum.m_exclusiveCount += treeNode.Caller.m_exclusiveCount;
                callerSum.m_exclusiveMetric += treeNode.Caller.m_exclusiveMetric;
                callerSum.m_inclusiveCount += inclusiveCountRet;
                callerSum.m_inclusiveMetric += inclusiveMetricRet;
                callerSum.m_exclusiveCount += callerTreeNode.m_exclusiveCount;
                callerSum.m_exclusiveMetric += callerTreeNode.m_exclusiveMetric;

                if (callerTreeNode.m_firstTimeRelMSec < callerSum.m_firstTimeRelMSec)
                    callerSum.m_firstTimeRelMSec = callerTreeNode.m_firstTimeRelMSec;
                if (callerTreeNode.m_lastTimeRelMSec > callerSum.m_lastTimeRelMSec)
                    callerSum.m_lastTimeRelMSec = callerTreeNode.m_lastTimeRelMSec;
            }
        }
    }

    /// <summary>
    /// Find the Caller-Callee treeNode in 'elems' with name 'frameName'.  Always succeeds because it
    /// creates one if necessary. 
    /// </summary>
    private CallTreeNodeBase Find(ref List<CallTreeNodeBase> elems, string frameName)
    {
        CallTreeNodeBase elem;
        for (int i = 0; i < elems.Count; i++)
        {
            elem = elems[i];
            if (elem.Name == frameName)
                return elem;
        }
        elem = new CallTreeNodeBase(frameName, StackSourceFrameIndex.Invalid, m_container);
        elems.Add(elem);
        return elem;
    }

    // state;
    private List<CallTreeNodeBase> m_callers;
    private List<CallTreeNodeBase> m_callees;
    #endregion
}
