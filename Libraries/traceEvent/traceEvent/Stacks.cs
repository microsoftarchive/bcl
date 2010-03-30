//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics.Eventing;
using Diagnostics.Eventing;

public enum SampleGroupCallStackIndex { Invalid = -1 };

/// <summary>
/// It is the abstract contract for a sample.  All we need is the metric and 
/// </summary>
public abstract class SampleGroups
{
    public SampleGroups(TraceLog log) { m_log = log; }
    public virtual float GetMetric(TraceEvent sample)
    {
        return 1;
    }
    public abstract SampleGroupCallStackIndex GetStack(TraceEvent sample);
    /// <summary>
    /// For efficiency, frames are assumed have a integer ID instead of a string name that
    /// is unique to the frame
    /// </summary>
    public abstract int GetFrameId(SampleGroupCallStackIndex callStackIndex);
    public abstract string GetFrameName(SampleGroupCallStackIndex callStackIndex);
    public abstract SampleGroupCallStackIndex GetCaller(SampleGroupCallStackIndex callStackIndex);
    #region private
    protected TraceLog m_log;
    #endregion
}

/// <summary>
/// This is the traditional grouping by method.
/// 
/// MethodSampleGroups create the folowing meaning for the code:SampleGroupCallStackIndex
/// 
/// * If the number is between 0 - m_log.CallStacks.MaxCallStackIndex -> Then it is a call stack index
/// * If after that the number (X - MaxCallStackIndex) encodes the thread and process of the sample.  
///     * (X - MaxCallStackIndex) % MaxProcessIndex  Indicates the process 
///     * (X - MaxCallStackIndex) / MaxProcessIndex  Indicates the process thread or process
///         * if 0 -> means the process
///         * if non-zero means the thread index 
/// 
/// </summary>
public class MethodSampleGroups : SampleGroups
{
    public MethodSampleGroups(TraceLog log) : base(log) { }
    public override SampleGroupCallStackIndex GetStack(TraceEvent sample)
    {
        m_sample = sample;      // TODO not pretty, can have only one sample 'in flight' at a time.  

        // Console.WriteLine("Getting Stack for sample at {0:f4}", sample.TimeStampRelativeMSec);
        var ret = (SampleGroupCallStackIndex)sample.CallStackIndex();
        return ret;
    }
    /// <summary>
    /// The frame ID consists of the following ranges concatinated together. 
    ///     * MaxMethodIndex - a method 
    ///     * MaxFileModuleIndex - a module
    ///     * MaxProcess - a process
    ///     * MaxThreadIndex - a thread
    ///     * -2 - a unknown location. 
    /// </summary>
    public override int GetFrameId(SampleGroupCallStackIndex callStackIndex)
    {
        int index = (int)callStackIndex;
        if (index < m_log.CallStacks.MaxCallStackIndex)
        {
            CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)callStackIndex);
            MethodIndex methodIndex = m_log.CallStacks.CodeAddresses.MethodIndex(codeAddressIndex);
            if (methodIndex != MethodIndex.Invalid)
                return (int)methodIndex;
            else
            {
                ModuleFileIndex moduleFileIdx = m_log.CodeAddresses.ModuleFileIndex(codeAddressIndex);
                if (moduleFileIdx != ModuleFileIndex.Invalid)
                    return m_log.CodeAddresses.Methods.MaxMethodIndex + (int)moduleFileIdx;
                else
                    return -2;      // Represents ?!? 
            }
        }
        index -= m_log.CallStacks.MaxCallStackIndex;

        int processIndex = index % m_log.Processes.MaxProcessIndex;
        int threadIndex = index / m_log.Processes.MaxProcessIndex;

        int baseIdx = m_log.CodeAddresses.Methods.MaxMethodIndex + m_log.CodeAddresses.ModuleFiles.MaxModuleFileIndex;
        if (threadIndex == 0)       // threadIndex of 0 is special, it means the process itself. 
            return baseIdx + processIndex;
        --threadIndex;

        TraceProcess process = m_log.Processes[(ProcessIndex)processIndex];
        baseIdx += m_log.Processes.MaxProcessIndex;

        Debug.Assert(threadIndex < process.Threads.MaxThreadIndex);
        return baseIdx + threadIndex;
    }
    public override string GetFrameName(SampleGroupCallStackIndex callStackIndex)
    {
        int index = (int)callStackIndex;
        if (index < m_log.CallStacks.MaxCallStackIndex)
        {
            CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)callStackIndex);

            string moduleName = "?";
            ModuleFileIndex moduleFileIndex = m_log.CodeAddresses.ModuleFileIndex(codeAddressIndex);
            if (moduleFileIndex != Diagnostics.Eventing.ModuleFileIndex.Invalid)
                moduleName = m_log.CodeAddresses.ModuleFiles[moduleFileIndex].Name;

            string methodName = "?";
            MethodIndex methodIndex = m_log.CodeAddresses.MethodIndex(codeAddressIndex);
            if (methodIndex != Diagnostics.Eventing.MethodIndex.Invalid)
                methodName = m_log.CodeAddresses.Methods.FullMethodName(methodIndex);
            return moduleName + "!" + methodName;
        }
        index -= m_log.CallStacks.MaxCallStackIndex;

        int processIndex = index % m_log.Processes.MaxProcessIndex;
        int threadIndex = index / m_log.Processes.MaxProcessIndex;

        TraceProcess process = m_log.Processes[(ProcessIndex)processIndex];
        if (threadIndex == 0)       // threadIndex of 0 is special, it means the process itself. 
            return "Process " + process.Name + "(" + process.ProcessID + ")";

        --threadIndex;
        if (threadIndex < process.Threads.MaxThreadIndex)
        {
            TraceThread thread = process.Threads[(ThreadIndex)threadIndex];
            return "Thread(" + thread.ThreadID + ")";
        }
        Debug.Assert(false, "Bad StackGroupIndex");
        return "";
    }
    public override SampleGroupCallStackIndex GetCaller(SampleGroupCallStackIndex callStackIndex)
    {
        int index = (int)callStackIndex;
        if (index < m_log.CallStacks.MaxCallStackIndex)
        {
            // We colapse direct recursion.   TODO: how to control this.  
            int frameId = GetFrameId(callStackIndex);
            for (; ; )
            {
                var nextCallStack = (SampleGroupCallStackIndex)m_log.CallStacks.Caller((CallStackIndex)callStackIndex);
                if (nextCallStack == SampleGroupCallStackIndex.Invalid)
                {
                    // return the ID for the thread.  
                    TraceThread thread = m_sample.Thread();
                    return (SampleGroupCallStackIndex)
                        (((int)thread.ThreadIndex + 1) * m_log.Processes.MaxProcessIndex) +
                        (int)thread.Process.ProcessIndex +
                        m_log.CallStacks.MaxCallStackIndex;
                }

                int nextFrameId = GetFrameId(nextCallStack);
                if (nextFrameId != frameId)
                    return nextCallStack;

                frameId = nextFrameId;
                callStackIndex = nextCallStack;
            }
        }
        index -= m_log.CallStacks.MaxCallStackIndex;

        int processIndex = index % m_log.Processes.MaxProcessIndex;
        int threadIndex = index / m_log.Processes.MaxProcessIndex;

        if (threadIndex == 0)       // threadIndex of 0 is special, it means the process itself, 
            return SampleGroupCallStackIndex.Invalid;       // Process has no parent

        Debug.Assert(processIndex < m_log.Processes.MaxProcessIndex);
        return (SampleGroupCallStackIndex)(processIndex + m_log.CallStacks.MaxCallStackIndex);
    }
    #region private
    TraceEvent m_sample;
    #endregion
}

/// <summary>
/// SampleInfos of a set of samples by eventToStack.  This represents the entire call tree.   You create an empty one in using
/// the default constructor and use 'AddSample' to add samples to it.   You traverse it by 
/// </summary>
public sealed class CallTree
{
    static public CallTree MethodCallTree(TraceEvents events)
    {
        return new CallTree(new MethodSampleGroups(events.Log), events);
    }
    public CallTree(SampleGroups sampleInfo, TraceEvents events)
    {
        m_top = new CallTreeNode("ROOT", -2, null, this);
        m_SampleInfo = sampleInfo;

        // Add all the events to the event tree
        foreach (var event_ in events)
        {
            // Find the bottom-most treeNode, updating the inclusive times along the way.  
            float metric = m_SampleInfo.GetMetric(event_);
            var callStackIndex = m_SampleInfo.GetStack(event_);
            UpdateInclusive(callStackIndex, metric, event_.TimeStampRelativeMSec, true);
        }

        // By default sort by inclusive metric
        SortInclusiveMetricDecending();
    }
    public CallTreeNode Top { get { return m_top; } }

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
    internal CallerCalleeNode CallerCallee(string nodeName)
    {
        return new CallerCalleeNode(nodeName, this);
    }

    /// <summary>
    /// Return a list of nodes that have statisicts rolled up by treeNode (eg method) name.  It is not
    /// sorted by anything in particular.  
    /// </summary>
    public IEnumerable<CallTreeBaseNode> SumByName { get { return GetSumById().Values; } }
    public List<CallTreeBaseNode> SumByNameSortedExclusiveMetric()
    {
        var ret = new List<CallTreeBaseNode>(SumByName);
        ret.Sort((x, y) => y.ExclusiveMetric.CompareTo(x.ExclusiveMetric));
        return ret;
    }
    public List<CallTreeBaseNode> SumByNameSortedInclusiveMetric()
    {
        var ret = new List<CallTreeBaseNode>(SumByName);
        ret.Sort((x, y) => y.InclusiveMetric.CompareTo(x.InclusiveMetric));
        return ret;
    }

    #region private
    /// <summary>
    /// Given the call stack represented by 'callStackIndex' (ordered from leaf method to top
    /// method) the metric to add to the treeNode, and whether this callStackIndex is the leafNode
    /// (where the sample actually happened), update all nodes in the tree from 'callStackIndex'
    /// to the root, and then return the CallTreeNode that represents the callerSum of 'callerStackIndex'
    /// </summary>
    internal CallTreeNode UpdateInclusive(SampleGroupCallStackIndex callStackIndex, float metric, double timeRelMsec, bool leafNode)
    {
        CallTreeNode myNode;
        if (callStackIndex == SampleGroupCallStackIndex.Invalid)
            myNode = m_top;
        else
        {
            SampleGroupCallStackIndex callerStackIndex = m_SampleInfo.GetCaller(callStackIndex);
            CallTreeNode callerNode = UpdateInclusive(callerStackIndex, metric, timeRelMsec, false);
            myNode = callerNode.FindCallee(callStackIndex);
        }

        if (leafNode)
        {
            myNode.m_exclusiveCount++;
            myNode.m_exclusiveMetric += metric;
        }
        myNode.m_inclusiveCount++;
        myNode.m_inclusiveMetric += metric;

        if (timeRelMsec < myNode.m_firstTimeRelMSec)
            myNode.m_firstTimeRelMSec = timeRelMsec;
        if (timeRelMsec > myNode.m_firstTimeRelMSec)
            myNode.m_lastTimeRelMSec = timeRelMsec;
        return myNode;
    }

    private Dictionary<int, CallTreeBaseNode> GetSumById()
    {
        if (m_sumById == null)
        {
            m_sumById = new Dictionary<int, CallTreeBaseNode>();
            var callersOnStack = new Dictionary<int, CallTreeBaseNode>();       // This is just a set
            AccumulateSumById(m_top, callersOnStack);
        }
        return m_sumById;
    }

    /// <summary>
    /// Traverse the subtree of 'treeNode' into the m_sumById dictionary.   We don't want to
    /// double-count inclusive times, so we have to keep track of all m_callers currently on the
    /// stack and we only add inclusive times for nodes that are not already on the stack.  
    /// </summary>
    private void AccumulateSumById(CallTreeNode treeNode, Dictionary<int, CallTreeBaseNode> callersOnStack)
    {
        CallTreeBaseNode byIdNode;
        if (!m_sumById.TryGetValue(treeNode.m_id, out byIdNode))
        {
            byIdNode = new CallTreeBaseNode(treeNode.m_name, treeNode.m_id, this);
            m_sumById.Add(treeNode.m_id, byIdNode);
        }

        bool newOnStack = !callersOnStack.ContainsKey(treeNode.m_id);
        // Add in the tree treeNode's contribution
        byIdNode.Combine(treeNode, newOnStack);
        if (treeNode.m_callees != null)
        {
            if (newOnStack)
                callersOnStack.Add(treeNode.m_id, null);
            foreach (var child in treeNode.m_callees)
                AccumulateSumById(child, callersOnStack);
            if (newOnStack)
                callersOnStack.Remove(treeNode.m_id);
        }
    }

    Dictionary<int, CallTreeBaseNode> m_sumById;
    internal SampleGroups m_SampleInfo;
    private CallTreeNode m_top;
    #endregion

}

/// <summary>
/// The part of a calltreeNode that is common to Caller-calleeSum and the calltree view.  
/// </summary>
public class CallTreeBaseNode
{
    public string Name { get { return m_name; } }
    public float InclusiveMetric { get { return m_inclusiveMetric; } }
    public float ExclusiveMetric { get { return m_exclusiveMetric; } }
    public float InclusiveCount { get { return m_inclusiveCount; } }
    public float ExclusiveCount { get { return m_exclusiveCount; } }

    public double FirstTimeRelMSec { get { return m_firstTimeRelMSec; } }
    public double LastTimeRelMSec { get { return m_lastTimeRelMSec; } }
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
    internal CallTreeBaseNode(string name, int id, CallTree container)
    {
        this.m_name = name;
        this.m_container = container;
        this.m_id = id;
        this.m_firstTimeRelMSec = Double.PositiveInfinity;
    }
    internal void Combine(CallTreeBaseNode other, bool addInclusive)
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
    }
    internal int m_id;
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
/// TODO should a sort the Callees by name, or inclusive metric?
/// </summary>
public sealed class CallTreeNode : CallTreeBaseNode
{
    public CallTreeNode Caller { get { return m_caller; } }
    public int CalleesCount { get { return m_callees.Count; } }
    public IEnumerable<CallTreeNode> Callees { get { return m_callees; } }
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
    internal CallTreeNode(string name, int id, CallTreeNode caller, CallTree container)
        : base(name, id, container)
    {
        this.m_caller = caller;
    }

    internal CallTreeNode FindCallee(SampleGroupCallStackIndex callStackIndex)
    {
        int frameId = m_container.m_SampleInfo.GetFrameId(callStackIndex);
        CallTreeNode callee;
        if (m_callees != null)
        {
            for (int i = 0; i < m_callees.Count; i++)
            {
                callee = m_callees[i];
                if (callee.m_id == frameId)
                    return callee;
            }
        }
        else
            m_callees = new List<CallTreeNode>();

        string frameName = m_container.m_SampleInfo.GetFrameName(callStackIndex);
        callee = new CallTreeNode(frameName, frameId, this, m_container);
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
/// grouping).   It takes all samples that have callStacks that include that treeNode and compute the metrics for
/// all the m_callers and all the m_callees for that treeNode.  
/// </summary>
class CallerCalleeNode : CallTreeBaseNode
{
    /// <summary>
    /// Given a complete call tree, and a Name within that call tree to focus on, create a
    /// CallerCalleeNode that represents the single Caller-Callee view for that treeNode. 
    /// </summary>
    public CallerCalleeNode(string nodeName, CallTree callTree)
        : base(nodeName, -1, callTree)
    {
        m_callees = new List<CallTreeBaseNode>();
        m_callers = new List<CallTreeBaseNode>();
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

        Debug.Assert(callerSum == m_inclusiveMetric);
        Debug.Assert(calleeSum + m_exclusiveMetric == m_inclusiveMetric);
#endif
    }

    public IEnumerable<CallTreeBaseNode> Callers { get { return m_callers; } }
    public IEnumerable<CallTreeBaseNode> Callees { get { return m_callees; } }
    public void ToXml(TextWriter writer, string indent)
    {
        writer.Write("{0}<CallerCallee", indent); this.ToXmlAttribs(writer); writer.WriteLine(">");
        writer.WriteLine("{0} <Callers Count=\"{1}\">", indent, m_callers.Count);
        foreach (CallTreeBaseNode caller in m_callers)
        {
            writer.Write("{0}  <Node", indent);
            caller.ToXmlAttribs(writer);
            writer.WriteLine("/>");
        }
        writer.WriteLine("{0} </Callers>", indent);
        writer.WriteLine("{0} <Callees Count=\"{1}\">", indent, m_callees.Count);
        foreach (CallTreeBaseNode callees in m_callees)
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
    /// Accumlate all the samples represented by 'treeNode' and all its children into the current
    /// CallerCalleeNode represention for 'this.Name' (called the focus treeNode) 'recursionCount' is
    /// the number of times 'this.Name' has been seen as Caller on the path to the root (not
    /// including 'treeNode' itself). This method returns the inclusive metric and the inclusive count
    /// for the calltree 'treeNode'. These inclusive metrics are weighted as described below.   
    /// 
    /// Recursive methods can easily cause double-counting in a Caller-Callee view for the
    /// inclusive times of the focus treeNode because one sample is counted in the inclusive time for
    /// every callee that contains a recusive path to the root. Thus one sample is being counted
    /// more than once.
    /// 
    /// To avoid this we 'split' the sample by the recursion count of focus method along every
    /// path.  These split samples are 'double counted' in the normal inclusive rollup, however
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
        bool isFocusNode = treeNode.Name.Equals(m_name);
        if (isFocusNode)
            recursionCount++;

        if (recursionCount > 0)
        {
            // Compute exclusive count and metric (and initialize the inclusive count and metric). 
            inclusiveCountRet = treeNode.ExclusiveMetric / recursionCount;
            inclusiveMetricRet = treeNode.ExclusiveMetric / recursionCount;
            if (isFocusNode)
            {
                m_exclusiveCount += inclusiveCountRet;
                m_exclusiveMetric += inclusiveMetricRet;
            }
        }

        // Get all the samples for the children and set the calleeSum information
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
                        CallTreeBaseNode calleeSum = Find(ref m_callees, calleeTreeNode.m_name);
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
                CallTreeBaseNode callerSum = Find(ref m_callers, callerTreeNode.m_name);
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
    private CallTreeBaseNode Find(ref List<CallTreeBaseNode> elems, string frameName)
    {
        CallTreeBaseNode elem;
        for (int i = 0; i < elems.Count; i++)
        {
            elem = elems[i];
            if (elem.Name == frameName)
                return elem;
        }
        elem = new CallTreeBaseNode(frameName, -1, m_container);
        elems.Add(elem);
        return elem;
    }

    // state;
    private List<CallTreeBaseNode> m_callers;
    private List<CallTreeBaseNode> m_callees;
    #endregion
}
