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
using Diagnostics.Eventing;

/// <summary>
/// This is the traditional grouping by method.
/// 
/// TraceEventStackSource create the folowing meaning for the code:StackSourceCallStackIndex
/// 
/// * If the number is less than 'Start' it is a speical pseudo-frame
/// * after that the number (X - Start) encodes a StackIndex
/// * If after that the number (X - MaxCallStackIndex) encodes the thread and process of the sample.  
///     * (X - MaxCallStackIndex) % MaxProcessIndex  Indicates the process 
///     * (X - MaxCallStackIndex) / MaxProcessIndex  Indicates the process thread or process
///         * if 0 -> means the process
///         * if non-zero means the thread index 
///         
/// TraceEventStackSource create the folowing meaning for the code:StackSourceFrameIndex
/// 
/// The frame ID consists of the following ranges concatinated together. 
///     * a small fixed number of Pseudo m_frames (Broken, and Unknown)
///     * MaxCodeAddressIndex - a method 
///     * MaxFileModuleIndex - a module
///     * (threadIndex+1) * MaxProcess + processIndex; 
///     
/// </summary>
public class TraceEventStackSource : StackSource
{
    public TraceEventStackSource() { }
    public TraceEventStackSource(TraceEvents events) {
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
            m_curSample = new StackSourceSample();
            m_curSample.Metric = 1;
        }
        m_curEventPos = ((IEnumerable<TraceEvent>)events).GetEnumerator();
    }

    public override StackSourceSample GetNextSample()
    {
        if (!m_curEventPos.MoveNext())
            return null;

        m_curEvent = m_curEventPos.Current;
        m_curSample.Stack = GetStack(m_curEvent);
        m_curSample.TimeRelMSec = m_curEvent.TimeStampRelativeMSec;
        m_curSample.ProcessID = m_curEvent.ProcessID;
        m_curSample.ThreadID = m_curEvent.ThreadID;
        m_curSample.ProcessName = m_curEvent.ProcessName;
        return m_curSample;
    }

    // see code:TraceEventStackSource for the encoding of StackSourceCallStackIndex and StackSourceFrameIndex
    public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
    {
        if (callStackIndex < StackSourceCallStackIndex.Start)
        {
            Debug.Assert(callStackIndex == StackSourceCallStackIndex.Broken);
            return StackSourceFrameIndex.Broken;
        }

        int index = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
        if (index < m_log.CallStacks.MaxCallStackIndex)
        {
            CodeAddressIndex codeAddressIndex = m_log.CallStacks.CodeAddressIndex((CallStackIndex)index);
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
        index -= m_log.CallStacks.MaxCallStackIndex;
        Debug.Assert(index < m_log.Processes.MaxProcessIndex * (MaxThreadsPerProc + 1));

        // At this point this is the encoded thread/process index.   We use the same encoding for both stacks and for frame names
        // so we just need to add back in the proper offset. 
        return (StackSourceFrameIndex)(index + m_log.CodeAddresses.MaxCodeAddressIndex + m_log.CodeAddresses.ModuleFiles.MaxModuleFileIndex + (int)StackSourceFrameIndex.Start);
    }
    // see code:TraceEventStackSource for the encoding of StackSourceCallStackIndex and StackSourceFrameIndex
    public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
    {
        if (callStackIndex < StackSourceCallStackIndex.Start)
        {
            Debug.Assert(callStackIndex == StackSourceCallStackIndex.Broken);
            return GetThreadCallStackIndex();
        }

        int index = (int)callStackIndex - (int)StackSourceCallStackIndex.Start;
        if (index < m_log.CallStacks.MaxCallStackIndex)
        {
            var nextCallStack = (StackSourceCallStackIndex)m_log.CallStacks.Caller((CallStackIndex)index);
            if (nextCallStack == StackSourceCallStackIndex.Invalid)
            {
                if (!ReasonableTopFrame(callStackIndex))
                    return StackSourceCallStackIndex.Broken;
                return GetThreadCallStackIndex();
            }
            return (StackSourceCallStackIndex)(nextCallStack + (int)StackSourceCallStackIndex.Start);
        }
        index -= m_log.CallStacks.MaxCallStackIndex;

        int processIndex = index % m_log.Processes.MaxProcessIndex;
        int threadIndex = index / m_log.Processes.MaxProcessIndex;

        if (threadIndex == 0)                               // threadIndex of 0 is special, it means the process itself, 
            return StackSourceCallStackIndex.Invalid;       // Process has no parent
        --threadIndex;                                      // recover the true thread index from the one with the process in it.  

        Debug.Assert(processIndex < m_log.Processes.MaxProcessIndex);
        // The process is the parent of the thread.    
        return (StackSourceCallStackIndex)(processIndex + m_log.CallStacks.MaxCallStackIndex + (int)StackSourceCallStackIndex.Start);
    }
    public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
    {
        string methodName = "?";
        var moduleFileIdx = ModuleFileIndex.Invalid;

        if (frameIndex < StackSourceFrameIndex.Start)
        {
            if (frameIndex == StackSourceFrameIndex.Broken)
                return "Broken";
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

                int processIndex = index % m_log.Processes.MaxProcessIndex;
                int threadIndex = index / m_log.Processes.MaxProcessIndex;

                var process = m_log.Processes[(ProcessIndex)processIndex];
                Debug.Assert(process.ProcessID == m_curEvent.ProcessID);

                if (threadIndex == 0)
                    return "Process " + process.Name + " (" + process.ProcessID + ")";
                --threadIndex;                                      // recover the true thread index from the one with the process in it.  

                Debug.Assert(threadIndex < process.Threads.MaxThreadIndex);
                TraceThread thread = process.Threads[(ThreadIndex)threadIndex];

                Debug.Assert(thread.ThreadID == m_curEvent.ThreadID);
                return "Thread (" + thread.ThreadID + ")";
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

    public override int MaxCallStackIndex
    {
        get
        {
            return m_log.CallStacks.MaxCallStackIndex + m_log.Processes.MaxProcessIndex * (MaxThreadsPerProc + 1) + (int)StackSourceCallStackIndex.Start;
        }
    }
    public override int MaxCallFrameIndex
    {
        get
        {
            return m_log.CodeAddresses.MaxCodeAddressIndex + m_log.CodeAddresses.ModuleFiles.MaxModuleFileIndex + m_log.Processes.MaxProcessIndex * (MaxThreadsPerProc + 1) +
               (int)StackSourceFrameIndex.Start;
        }
    }

    #region private
    private int MaxThreadsPerProc
    {
        get
        {
            if (m_maxThreadsPerProc == 0)
            {
                foreach (var proc in m_log.Processes)
                {
                    if (m_maxThreadsPerProc < proc.Threads.MaxThreadIndex)
                        m_maxThreadsPerProc = proc.Threads.MaxThreadIndex;
                }
            }
            return m_maxThreadsPerProc;
        }
    }

    private StackSourceCallStackIndex GetStack(TraceEvent event_)
    {
        m_curEvent = event_;      // TODO not pretty, can have only one sample 'in flight' at a time.  

        // Console.WriteLine("Getting Stack for sample at {0:f4}", sample.TimeStampRelativeMSec);
        var ret = (StackSourceCallStackIndex)m_curEvent.CallStackIndex();
        if (ret == StackSourceCallStackIndex.Invalid)
            ret = GetThreadCallStackIndex();
        else
            ret = ret + (int)StackSourceCallStackIndex.Start;
        return ret;
    }
    private bool ReasonableTopFrame(StackSourceCallStackIndex callStackIndex)
    {
        uint index = (uint)callStackIndex - (uint)StackSourceCallStackIndex.Start;
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
    /// <summary>
    /// Gets the node representing the thread as a whole.  
    /// </summary>
    /// <returns></returns>
    private StackSourceCallStackIndex GetThreadCallStackIndex()
    {
        // We have run  out of true stack m_frames, we use the thread as the parent of the stack. 
        TraceThread thread = m_curEvent.Thread();
        if (thread == null)     // Should never happen
            return StackSourceCallStackIndex.Invalid;

        TraceProcess process = thread.Process;
        if (process == null)    // Should never happen
            return StackSourceCallStackIndex.Invalid;

        var ret = (StackSourceCallStackIndex)((((int)thread.ThreadIndex + 1) * m_log.Processes.MaxProcessIndex) +
            (int)process.ProcessIndex + m_log.CallStacks.MaxCallStackIndex) + (int)StackSourceCallStackIndex.Start;
        Debug.Assert((int)ret < MaxCallStackIndex);
        return ret;
    }

    TraceEvent m_curEvent;      // TODO remove
    StackSourceSample m_curSample;
    IEnumerator<TraceEvent> m_curEventPos;
    int m_maxThreadsPerProc;
    ModuleFileIndex m_goodTopModuleIndex;       // This is a known good module index for a 'good' stack (probably ntDll!RtlUserStackStart
    // TODO currently method id don't encode their module, but we want them to.  As a result
    // we use the code address of any address in the method as the representation for the method
    // (which DOES know the module), this array remembers which address we used
    int[] m_codeAddrForMethod;
    protected TraceLog m_log;
    #endregion
}
