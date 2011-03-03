// Copyright (c) Microsoft Corporation.  All rights reserved
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
using Diagnostics.Eventing;
using Stacks;

/// <summary>
/// This is the traditional grouping by method.
/// 
/// TraceEventStackSource create the folowing meaning for the code:StackSourceCallStackIndex
/// 
/// * The call stacks ID consists of the following ranges concatinated together. 
///     * a small set of fixed Pseuo stacks (Start marks the end of these)
///     * CallStackIndex
///     * ThreadIndex
///     * ProcessIndex
///     * BrokenStacks (One per thread)
///         
/// TraceEventStackSource create the folowing meaning for the code:StackSourceFrameIndex
/// 
/// The frame ID consists of the following ranges concatinated together. 
///     * a small fixed number of Pseudo frame (Broken, and Unknown)
///     * MaxCodeAddressIndex - a known method (we can't use methodIndex because it does not know what DLL it comes from).  
///         However we use the same CodeAddress for all samples within the method (thus it is effectivley a MethodIndex).  
///     * MaxFileModuleIndex - an unknown method in a module.  
///     * ThreadIndex
///     * ProcessIndex
///     
/// </summary>
public class TraceEventStackSource : StackSource
{
    public TraceEventStackSource() { }
    public TraceEventStackSource(TraceEvents events)
    {
        SetEvents(events);
    }
    public void SetEvents(TraceEvents events)
    {
        if (m_log != events.Log)
        {
            Debug.Assert(m_log == null);
            m_log = events.Log;
            var newArray = new int[m_log.CodeAddresses.Methods.MaxMethodIndex];
            for (int i = 0; i < newArray.Length; i++)
                newArray[i] = -1;
            m_codeAddrForMethod = newArray;
            m_goodTopModuleIndex = ModuleFileIndex.Invalid;
            m_curSample = new StackSourceSample(this);
            m_curSample.Metric = 1;
        }
        m_events = events;
    }

    public override void ProduceSamples(Action<StackSourceSample> callback)
    {
        // TODO use callback model rather than enumerator
        foreach (var event_ in ((IEnumerable<TraceEvent>)m_events))
        {
            m_curSample.StackIndex = GetStack(event_);
            m_curSample.TimeRelMSec = event_.TimeStampRelativeMSec;
            Debug.Assert(event_.ProcessName != null);
            callback(m_curSample);
        }
    }
    public override double SampleTimeRelMSecLimit
    {
        get
        {
            return m_log.SessionEndTime100ns;
        }
    }

    // see code:TraceEventStackSource for the encoding of StackSourceCallStackIndex and StackSourceFrameIndex
    public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
    {
        Debug.Assert(callStackIndex >= 0);
        Debug.Assert(StackSourceCallStackIndex.Start == 0);         // If there are any cases before start, we need to handle them here. 

        int stackIndex = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
        if (stackIndex < m_log.CallStacks.MaxCallStackIndex)
        {
            CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)stackIndex);
            MethodIndex methodIndex = m_log.CallStacks.CodeAddresses.MethodIndex(codeAddressIndex);
            if (methodIndex != MethodIndex.Invalid)
            {
                var firstCodeAddressIndex = m_codeAddrForMethod[(int)methodIndex];
                if (firstCodeAddressIndex < 0)
                {
                    firstCodeAddressIndex = (int)codeAddressIndex;
                    m_codeAddrForMethod[(int)methodIndex] = firstCodeAddressIndex;
                }
                return (StackSourceFrameIndex)(firstCodeAddressIndex + (int)StackSourceFrameIndex.Start);
            }
            else
            {
                ModuleFileIndex moduleFileIdx = m_log.CodeAddresses.ModuleFileIndex(codeAddressIndex);
                if (moduleFileIdx != ModuleFileIndex.Invalid)
                    return (StackSourceFrameIndex)(m_log.CodeAddresses.MaxCodeAddressIndex + (int)moduleFileIdx + (int)StackSourceFrameIndex.Start);
                else
                    return StackSourceFrameIndex.Unknown;
            }
        }
        stackIndex -= m_log.CallStacks.MaxCallStackIndex;
        if (stackIndex < m_log.Threads.MaxThreadIndex + m_log.Processes.MaxProcessIndex)
        {
            // At this point this is the encoded thread/process index.   We use the same encoding for both stacks and for frame names
            // so we just need to add back in the proper offset. 
            return (StackSourceFrameIndex)(stackIndex + m_log.CodeAddresses.MaxCodeAddressIndex + m_log.CodeAddresses.ModuleFiles.MaxModuleFileIndex + (int)StackSourceFrameIndex.Start);
        }

        Debug.Assert(stackIndex < 2 * m_log.Threads.MaxThreadIndex + m_log.Processes.MaxProcessIndex, "Illegal Frame Index");
        return StackSourceFrameIndex.Broken;
    }
    // see code:TraceEventStackSource for the encoding of StackSourceCallStackIndex and StackSourceFrameIndex
    public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
    {
        Debug.Assert(callStackIndex >= 0);
        Debug.Assert(StackSourceCallStackIndex.Start == 0);         // If there are any cases before start, we need to handle them here. 

        int curIndex = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
        int nextIndex = (int)StackSourceCallStackIndex.Start;
        if (curIndex < m_log.CallStacks.MaxCallStackIndex)
        {
            var nextCallStackIndex = m_log.CallStacks.Caller((CallStackIndex)curIndex);
            if (nextCallStackIndex == CallStackIndex.Invalid)
            {
                nextIndex += m_log.CallStacks.MaxCallStackIndex;    // Now points at the threads region.  
                var threadIndex = m_log.CallStacks.Thread((CallStackIndex)curIndex);
                nextIndex += (int)threadIndex;

                // Mark it as a broken stack, which come after all the indexes for normal threads and processes. 
                if (!ReasonableTopFrame(callStackIndex))
                    nextIndex += m_log.Threads.MaxThreadIndex + m_log.Processes.MaxProcessIndex;
            }
            else
                nextIndex += (int)nextCallStackIndex;
            return (StackSourceCallStackIndex)nextIndex;
        }
        curIndex -= m_log.CallStacks.MaxCallStackIndex;                                 // Now is a thread index
        nextIndex += m_log.CallStacks.MaxCallStackIndex;                                // Output index points to the thread region.          

        if (curIndex < m_log.Threads.MaxThreadIndex)
        {
            nextIndex += m_log.Threads.MaxThreadIndex;                                  // Output index point to process region.
            nextIndex += (int)m_log.Threads[(ThreadIndex)curIndex].Process.ProcessIndex;
            return (StackSourceCallStackIndex)nextIndex;
        }
        curIndex -= m_log.Threads.MaxThreadIndex;                                      // Now is a broken thread index

        if (curIndex < m_log.Processes.MaxProcessIndex)
            return StackSourceCallStackIndex.Invalid;                                   // Process has no parent
        curIndex -= m_log.Processes.MaxProcessIndex;                                    // Now is a broken thread index

        if (curIndex < m_log.Threads.MaxThreadIndex)                                    // It is a broken stack
        {
            nextIndex += curIndex;                                                      // Indicate the real thread.  
            return (StackSourceCallStackIndex)nextIndex;
        }
        Debug.Assert(false, "Invalid CallStackIndex");
        return StackSourceCallStackIndex.Invalid;
    }
    public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
    {
        string methodName = "?";
        var moduleFileIdx = ModuleFileIndex.Invalid;

        if (frameIndex < StackSourceFrameIndex.Start)
        {
            if (frameIndex == StackSourceFrameIndex.Broken)
                return "BROKEN";
            else if (frameIndex == StackSourceFrameIndex.Overhead)
                return "OVERHEAD";
            else if (frameIndex == StackSourceFrameIndex.Root)
                return "ROOT";
            else
                return "?!?";
        }
        int index = (int)frameIndex - (int)StackSourceFrameIndex.Start;
        if (index < m_log.CodeAddresses.MaxCodeAddressIndex)
        {
            var codeAddressIndex = (CodeAddressIndex)index;
            MethodIndex methodIndex = m_log.CallStacks.CodeAddresses.MethodIndex(codeAddressIndex);
            Debug.Assert(methodIndex != MethodIndex.Invalid);
            methodName = m_log.CodeAddresses.Methods.FullMethodName(methodIndex);
            moduleFileIdx = m_log.CodeAddresses.ModuleFileIndex(codeAddressIndex);
        }
        else
        {
            index -= m_log.CodeAddresses.MaxCodeAddressIndex;
            if (index < m_log.ModuleFiles.MaxModuleFileIndex)
                moduleFileIdx = (ModuleFileIndex)index;
            else
            {
                index -= m_log.ModuleFiles.MaxModuleFileIndex;

                if (index < m_log.Threads.MaxThreadIndex)
                {
                    TraceThread thread = m_log.Threads[(ThreadIndex)index];
                    return "Thread (" + thread.ThreadID + ")";
                }
                index -= m_log.Threads.MaxThreadIndex;
                if (index < m_log.Processes.MaxProcessIndex)
                {
                    TraceProcess process = m_log.Processes[(ProcessIndex)index];
                    return "Process " + process.Name + " (" + process.ProcessID + ")";
                }
                Debug.Assert(false, "Illegal Frame index");
                return "";
            }
        }

        string moduleName = "?";
        if (moduleFileIdx != ModuleFileIndex.Invalid)
        {
            if (fullModulePath)
            {
                moduleName = m_log.CodeAddresses.ModuleFiles[moduleFileIdx].FileName;
                int lastDot = moduleName.LastIndexOf('.');
                if (lastDot > 0)
                    moduleName = moduleName.Substring(0, lastDot);
            }
            else
                moduleName = m_log.CodeAddresses.ModuleFiles[moduleFileIdx].Name;
        }

        return moduleName + "!" + methodName;
    }

    public override int CallStackIndexLimit
    {
        get
        {
            return (int)StackSourceCallStackIndex.Start + m_log.CallStacks.MaxCallStackIndex +
                2 * m_log.Threads.MaxThreadIndex + m_log.Processes.MaxProcessIndex;     // *2 one for normal threads, one for broken threads.  
        }
    }
    public override int CallFrameIndexLimit
    {
        get
        {
            return (int)StackSourceFrameIndex.Start + m_log.CodeAddresses.MaxCodeAddressIndex + m_log.CodeAddresses.ModuleFiles.MaxModuleFileIndex +
                m_log.Threads.MaxThreadIndex + m_log.Processes.MaxProcessIndex;
        }
    }

    #region private

    private StackSourceCallStackIndex GetStack(TraceEvent event_)
    {
        // Console.WriteLine("Getting Stack for sample at {0:f4}", sample.TimeStampRelativeMSec);
        var ret = (int)event_.CallStackIndex();
        if (ret == (int)CallStackIndex.Invalid)
            ret = m_log.CallStacks.MaxCallStackIndex + (int)event_.Thread().ThreadIndex;
        ret = ret + (int)StackSourceCallStackIndex.Start;
        return (StackSourceCallStackIndex)ret;
    }
    private bool ReasonableTopFrame(StackSourceCallStackIndex callStackIndex)
    {

        uint index = (uint)callStackIndex - (uint)StackSourceCallStackIndex.Start;

        var stack = m_log.CallStacks[(CallStackIndex)callStackIndex];
        if (index < (uint)m_log.CallStacks.MaxCallStackIndex)
        {
            CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)index);
            ModuleFileIndex moduleFileIndex = m_log.CallStacks.CodeAddresses.ModuleFileIndex(codeAddressIndex);
            if (m_goodTopModuleIndex == moduleFileIndex)        // optimization
                return true;

            TraceModuleFile moduleFile = m_log.CallStacks.CodeAddresses.ModuleFile(codeAddressIndex);
            if (moduleFile == null || !moduleFile.FileName.EndsWith("ntdll.dll", StringComparison.OrdinalIgnoreCase))
                return false;

            m_goodTopModuleIndex = moduleFileIndex;
            return true;
        }
        return false;
    }

    StackSourceSample m_curSample;
    TraceEvents m_events;
    ModuleFileIndex m_goodTopModuleIndex;       // This is a known good module index for a 'good' stack (probably ntDll!RtlUserStackStart
    // TODO currently method id don't encode their module, but we want them to.  As a result
    // we use the code address of any address in the method as the representation for the method
    // (which DOES know the module), this array remembers which address we used
    int[] m_codeAddrForMethod;
    protected TraceLog m_log;
    #endregion
}
