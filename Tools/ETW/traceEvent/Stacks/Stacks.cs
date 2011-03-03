#define TOSTRING_FTNS

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
using System.Text;

namespace Stacks
{
    /// <summary>
    /// It is the abstract contract for a sample.  All we need is the Metric and 
    /// </summary>    
    public abstract class StackSource : StackSourceStacks
    {
        // Generate the samples.  The callback will be called for each sample.   The callback is not allowed to modify the sample
        // and can not cache the sample past the return of the callback (we reuse the StackSourceSample on each call)
        public abstract void ProduceSamples(Action<StackSourceSample> callback);

        // These are optional
        /// <summary>
        /// If this stack source is a source that simply groups another source, get tht base source.  It will return
        /// itself if there is no base source.  
        /// </summary>
        // TODO does this belong on StackSourceStacks?
        public virtual StackSource BaseStackSource { get { return this; } }
        /// <summary>
        /// If this source supports fetching the samples by index, this is how you get it.  Like ProduceSamples the sample that
        /// is returned is not allowed to be modified.   Also the returned sample will become invalid the next time GetSampleIndex
        /// is called (we reuse the StackSourceSample on each call)
        /// </summary>
        public virtual StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex) { return null; }
        public virtual int SampleIndexLimit { get { return 0; } }
        public virtual double SampleTimeRelMSecLimit { get { return 0; } }
    }

    /// <summary>
    /// Samples have stacks (lists of frames, each frame contains a name) associated with them.  This interface allows you to get 
    /// at this information.  We don't use normal objects to represent these but rather give each stack (and frame) a unique
    /// (dense) index.   This has a number of advantages over using objects to represent the stack.
    /// 
    ///     * Indexes are very serialization friendly, and this data will be presisted.  Thus indexes are the natural form for data on disk. 
    ///     * It allows the data to be read from the serialized format (disk) lazily in a very straightfoward fashion, keeping only the
    ///         hottest elements in memory.  
    ///     * Users of this API can associate additional data with the call stacks or frames trivially and efficiently simply by
    ///         having an array indexed by the stack or frame index.   
    ///         
    /// So effecively a StackSourceStacks is simply a set of 'Get' methods that allow you to look up information given a Stack or
    /// frame index.  
    /// </summary>
    public abstract class StackSourceStacks
    {
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
        /// FilterStackSources can combine more than one frame into a given frame.  It is useful to know
        /// how many times this happened.   Returning 0 means no combining happened.  
        /// </summary>
        public virtual int GetNumberOfFoldedFrames(StackSourceCallStackIndex callStackIndex)
        {
            return 0;
        }
        /// <summary>
        /// Get the frame name from the FrameIndex.   If 'verboseName' is true then full module path is included.
        /// </summary>
        public abstract string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName);
        /// <summary>
        /// all StackSourceCallStackIndex are guarenteed to be less than this.  Allocate an array of this size to associate side information
        /// </summary>
        public abstract int CallStackIndexLimit { get; }
        /// <summary>
        /// all StackSourceFrameIndex are guarenteed to be less than this.  Allocate an array of this size to associate side information
        /// </summary>
        public abstract int CallFrameIndexLimit { get; }

        public int StackDepth(StackSourceCallStackIndex callStackIndex)
        {
            int ret = 0;
            while (callStackIndex != StackSourceCallStackIndex.Invalid)
            {
                callStackIndex = GetCallerIndex(callStackIndex);
                ret++;
            }
            return ret;
        }

#if TOSTRING_FTNS
        // For debugging. 
        public string ToString(StackSourceSample sample)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<StackSourceSample");
            sb.Append(" Metric=\"").Append(sample.Metric.ToString("f1")).Append('"');
            sb.Append(" TimeRelMSec=\"").Append(sample.TimeRelMSec.ToString("f3")).Append('"');
            sb.Append(" SampleIndex=\"").Append(sample.SampleIndex.ToString()).Append('"');
            sb.Append(">").AppendLine();
            sb.AppendLine(ToString(sample.StackIndex));
            sb.Append("</StackSourceSample>");
            return sb.ToString();
        }
        public string ToString(StackSourceCallStackIndex callStackIndex)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(" <CallStack Index =\"").Append((int)callStackIndex).Append("\">").AppendLine();
            for (int i = 0; callStackIndex != StackSourceCallStackIndex.Invalid; i++)
            {
                if (i >= 300)
                {
                    sb.AppendLine("  <Truncated/>");
                    break;
                }
                sb.Append(ToString(GetFrameIndex(callStackIndex), callStackIndex)).AppendLine();
                callStackIndex = GetCallerIndex(callStackIndex);
            }
            sb.Append(" </CallStack>");
            return sb.ToString();
        }
        public string ToString(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex stackIndex = StackSourceCallStackIndex.Invalid)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("  <Frame");
            if (stackIndex != StackSourceCallStackIndex.Invalid)
                sb.Append(" StackID=\"").Append(((int)stackIndex).ToString()).Append("\"");
            sb.Append(" FrameID=\"").Append(((int)frameIndex).ToString()).Append("\"");
            sb.Append(" Name = \"").Append(GetFrameName(frameIndex, false)).Append("\"");
            sb.Append("/>");
            return sb.ToString();
        }
#endif
    }

    /// <summary>
    /// StackSourceSample represents a single sample that has a stack.  StackSource.GetNextSample returns these.  
    /// </summary>
    public class StackSourceSample
    {
        public StackSourceCallStackIndex StackIndex { get; set; }
        public float Metric { get; set; }

        // The rest of these are optional.  
        public StackSourceSampleIndex SampleIndex { get; set; }  // This identifies the sample uniquely in the source.  
        public double TimeRelMSec { get; set; }

#if TOSTRING_FTNS
        public override string ToString()
        {
            return String.Format("<Sample Metric=\"{0:f1}\" TimeRelMSec=\"{1:f3}\" StackIndex=\"{2}\" SampleIndex=\"{3}\">", 
                Metric, TimeRelMSec, StackIndex, SampleIndex);
        }
        public string ToString(StackSource source)
        {
            return source.ToString(this);
        }
#endif

        #region protected
        public StackSourceSample(StackSourceStacks source) { SampleIndex = StackSourceSampleIndex.Invalid; }
        public StackSourceSample(StackSourceSample template)
        {
            StackIndex = template.StackIndex;
            Metric = template.Metric;
            TimeRelMSec = template.TimeRelMSec;
            SampleIndex = template.SampleIndex;
        }
        #endregion
    }

    /// <summary>
    /// Identifies a particular sample from the sample source, it allows 3rd parties to attach additional
    /// information to the sample by creating an array indexed by sampleIndex.  
    /// </summary>
    public enum StackSourceSampleIndex { Invalid = -1 };

    /// <summary>
    /// An opaque handle that are 1-1 with a complete call stack
    /// 
    /// </summary>
    public enum StackSourceCallStackIndex
    {
        Start = 0,             // The first real call stack index (after the pseudo-ones before this)
        Invalid = -1,          // Returned when there is no caller (top of stack)
    };

    /// <summary>
    /// Identifies a particular frame within a stack   It represents a particular instruction pointer (IP) location 
    /// in the code or a group of such locations.  
    /// </summary>
    public enum StackSourceFrameIndex
    {
        Root = 0,              // Pseduo-node representing the root of all stacks
        Broken = 1,            // Pseduo-frame that represents the caller of all broken stacks. 
        Unknown = 2,           // Unknown what to do (Must be before the 'special ones below')  // Non negative represents normal m_frames (e.g. names of methods)
        Overhead = 3,          // Profiling overhead (rundown)
        Start = 4,             // The first real call stack index (after the pseudo-ones before this)

        Invalid = -1,           // Should not happen (uninitialized)
        Discard = -2,           // Sample has been filtered out (useful for filtering stack sources)
    };

    /// <summary>
    /// This stack source takes another and copies out all its events.   This allows you to 'replay' the source 
    /// efficiently when the original source only does this inefficiently.  
    /// </summary>
    public class CopyStackSource : StackSource
    {
        public CopyStackSource() { }
        public CopyStackSource(StackSource source)
        {
            m_source = source;
            source.ProduceSamples(delegate(StackSourceSample sample)
            {
                var sampleCopy = new StackSourceSample(sample);
                sampleCopy.SampleIndex = (StackSourceSampleIndex)m_samples.Count;
                m_samples.Add(sampleCopy);
                if (sampleCopy.TimeRelMSec > m_sampleTimeRelMSecLimit)
                    m_sampleTimeRelMSecLimit = sampleCopy.TimeRelMSec;
            });
        }

        // Support for sample indexes.  This allows you to look things up information in the sample
        // after being processed the first time.
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            return m_samples[(int)sampleIndex];
        }
        public override int SampleIndexLimit
        {
            get { return m_samples.Count; }
        }

        public override double SampleTimeRelMSecLimit { get { return m_sampleTimeRelMSecLimit; } }
        public override void ProduceSamples(Action<StackSourceSample> callback)
        {
            for (int i = 0; i < m_samples.Count; i++)
                callback(m_samples[i]);
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_source.GetCallerIndex(callStackIndex);
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_source.GetFrameIndex(callStackIndex);
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
        {
            return m_source.GetFrameName(frameIndex, fullModulePath);
        }

        public override int CallStackIndexLimit
        {
            get { if (m_source == null) return 0; return m_source.CallStackIndexLimit; }
        }
        public override int CallFrameIndexLimit
        {
            get { if (m_source == null) return 0; return m_source.CallFrameIndexLimit; }
        }

        #region private
        protected GrowableArray<StackSourceSample> m_samples;
        protected double m_sampleTimeRelMSecLimit;
        protected StackSource m_source;
        #endregion
    }

    /// <summary>
    /// Like CopyStackSource InternStackSource copies the samples. however unlike CopyStackSource
    /// InternStackSource copies all the information in the stacks too (mapping stack indexes to names)
    /// Thus it never refers to the original source again).   It also interns the stacks making for 
    /// an efficient representation of the data.   This is useful when the original source is expensive 
    /// to iterate over.   
    /// 
    /// If you have 'raw' uninterned data, subclassing InternStackSource 
    /// </summary>
    public class InternStackSource : CopyStackSource    // TODO FIX NOW: should this inherit from CopyStackSource?
    {

#if false   // TODO FIX NOW remove?
        public InternStackSource(StackSource source)
            : this()
        {
            ReadAllSamples(source, 1.0F);
            CompletedReading();
        }
#endif 
        /// <summary>
        /// Compute only the delta of soruce from the baseline. 
        /// </summary>
        public static InternStackSource Diff(StackSource source, StackSource baselineSource)
        {
            var ret = new InternStackSource();
            ret.ReadAllSamples(source, 1.0F);
            ret.ReadAllSamples(baselineSource, -1.0F);
            ret.CompletedReading();
            return ret;
        }

        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_callStacks[(int)callStackIndex].callerIndex;
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            return m_callStacks[(int)callStackIndex].frameIndex;
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullModulePath)
        {
            var framesIndex = (int)(frameIndex - StackSourceFrameIndex.Start);
            var frameName = m_frames[framesIndex].FrameName;
            var moduleName = m_modules[(int)m_frames[framesIndex].ModuleIndex];
            if (moduleName.Length == 0)
                return frameName;

            if (!fullModulePath)
            {
                var index = moduleName.LastIndexOf('\\');
                if (index >= 0)
                    moduleName = moduleName.Substring(index + 1);
            }
            return moduleName + "!" + frameName;
        }
        public override int CallStackIndexLimit
        {
            get { return m_callStacks.Count; }
        }
        public override int CallFrameIndexLimit
        {
            get { return (int)(StackSourceFrameIndex.Start + m_frames.Count); }
        }

        #region protected
        protected InternStackSource()
        {
            m_modules = new GrowableArray<string>(100);
            m_frames = new GrowableArray<FrameInfo>(1000);
            m_callStacks = new GrowableArray<CallStackInfo>(5000);
            m_moduleIntern = new Dictionary<string, StackSourceModuleIndex>(100);
            m_frameIntern = new Dictionary<FrameInfo, StackSourceFrameIndex>(1000);
            m_callStackIntern = new GrowableArray<GrowableArray<StackSourceCallStackIndex>>(5000);
            m_callStackIntern.Add(new GrowableArray<StackSourceCallStackIndex>(4));     // For the root
        }

        protected void ReadAllSamples(StackSource source, float scaleFactor)
        {
            source.ProduceSamples(delegate(StackSourceSample sample)
            {
                var sampleCopy = new StackSourceSample(sample);
                if (scaleFactor != 1.0F)
                    sampleCopy.Metric *= scaleFactor;
                sampleCopy.SampleIndex = (StackSourceSampleIndex)m_samples.Count;
                sampleCopy.StackIndex = CallStackIntern(source, sampleCopy.StackIndex);
                m_samples.Add(sampleCopy);
                if (sampleCopy.TimeRelMSec > m_sampleTimeRelMSecLimit)
                    m_sampleTimeRelMSecLimit = sampleCopy.TimeRelMSec;
            });
        }

        protected void CompletedReading()
        {
            m_moduleIntern = null;
            m_frameIntern = null;
            m_callStackIntern = new GrowableArray<GrowableArray<StackSourceCallStackIndex>>();
        }
        protected StackSourceModuleIndex ModuleIntern(string moduleName)
        {
            StackSourceModuleIndex ret;
            if (!m_moduleIntern.TryGetValue(moduleName, out ret))
            {
                ret = (StackSourceModuleIndex)m_modules.Count;
                m_modules.Add(moduleName);
                m_moduleIntern.Add(moduleName, ret);
            }
            return ret;
        }
        protected StackSourceFrameIndex FrameIntern(string frameName, StackSourceModuleIndex moduleIndex)
        {
            StackSourceFrameIndex ret;
            FrameInfo frame = new FrameInfo(frameName, moduleIndex);
            if (!m_frameIntern.TryGetValue(frame, out ret))
            {
                ret = (StackSourceFrameIndex.Start + m_frames.Count);
                m_frames.Add(frame);
                m_frameIntern.Add(frame, ret);
            }
            return ret;
        }
        protected StackSourceCallStackIndex CallStackIntern(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex callerIndex)
        {
            var frameCallees = m_callStackIntern[(int)callerIndex + 1];           // invalid (root) == -1 now maps to 0)
            if (frameCallees.Count == 0)
                frameCallees = new GrowableArray<StackSourceCallStackIndex>(4);

            // Search backwards, assuming that most recently added is the most likely hit.  
            for (int i = frameCallees.Count - 1; i >= 0; --i)
            {
                StackSourceCallStackIndex calleeIndex = frameCallees[i];
                if (m_callStacks[(int)calleeIndex].frameIndex == frameIndex)
                {
                    Debug.Assert(calleeIndex > callerIndex);
                    return calleeIndex;
                }
            }
            StackSourceCallStackIndex ret = (StackSourceCallStackIndex)(m_callStacks.Count);
            m_callStacks.Add(new CallStackInfo(frameIndex, callerIndex));
            frameCallees.Add(ret);
            m_callStackIntern[(int)callerIndex + 1] = frameCallees;                       // update my caller's information. 

            m_callStackIntern.Add(new GrowableArray<StackSourceCallStackIndex>());      // Entry for new retured index.  
            Debug.Assert(m_callStackIntern.Count == m_callStacks.Count + 1);
            return ret;
        }
        protected StackSourceCallStackIndex CallStackIntern(StackSource source, StackSourceCallStackIndex baseCallStackIndex)
        {
            if (baseCallStackIndex == StackSourceCallStackIndex.Invalid)
                return StackSourceCallStackIndex.Invalid;

            var baseCaller = source.GetCallerIndex(baseCallStackIndex);
            var baseFrame = source.GetFrameIndex(baseCallStackIndex);

            var baseFullFrameName = source.GetFrameName(baseFrame, true);
            var moduleName = "";
            var frameName = baseFullFrameName;
            var index = baseFullFrameName.IndexOf('!');
            if (index >= 0)
            {
                moduleName = baseFullFrameName.Substring(0, index);
                frameName = baseFullFrameName.Substring(index + 1);
            }

            var myModuleIndex = ModuleIntern(moduleName);
            var myFrameIndex = FrameIntern(frameName, myModuleIndex);
            var ret = CallStackIntern(myFrameIndex, CallStackIntern(source, baseCaller));
            return ret;
        }

        protected enum StackSourceModuleIndex { Invalid = -1 };

        protected struct FrameInfo : IEquatable<FrameInfo>
        {
            public FrameInfo(string frameName, StackSourceModuleIndex moduleIndex)
            {
                this.ModuleIndex = moduleIndex;
                this.FrameName = frameName;
            }
            public readonly StackSourceModuleIndex ModuleIndex;
            public readonly string FrameName;

            public override int GetHashCode()
            {
                return (int)ModuleIndex + FrameName.GetHashCode();
            }
            public override bool Equals(object obj) { throw new NotImplementedException(); }
            public bool Equals(FrameInfo other)
            {
                return ModuleIndex == other.ModuleIndex && FrameName == other.FrameName;
            }
        }
        protected struct CallStackInfo
        {
            public CallStackInfo(StackSourceFrameIndex frameIndex, StackSourceCallStackIndex callerIndex)
            {
                this.frameIndex = frameIndex;
                this.callerIndex = callerIndex;
            }
            public readonly StackSourceFrameIndex frameIndex;
            public readonly StackSourceCallStackIndex callerIndex;
        };

        protected GrowableArray<string> m_modules;
        protected GrowableArray<FrameInfo> m_frames;
        protected GrowableArray<CallStackInfo> m_callStacks;

        protected GrowableArray<GrowableArray<StackSourceCallStackIndex>> m_callStackIntern;
        protected Dictionary<FrameInfo, StackSourceFrameIndex> m_frameIntern;
        protected Dictionary<string, StackSourceModuleIndex> m_moduleIntern;

        #endregion
    }

    public class CallersStackSource : StackSource
    {
        public CallersStackSource(string nodeName, StackSource baseStackSource)
        {
            m_baseStackSource = baseStackSource;
            m_nodeName = nodeName;
        }
        public override void ProduceSamples(Action<StackSourceSample> callback)
        {
            m_baseStackSource.ProduceSamples(callback);
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            throw new NotImplementedException();
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            throw new NotImplementedException();
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            return m_baseStackSource.GetFrameName(frameIndex, verboseName);
        }
        public override int CallStackIndexLimit
        {
            get { return m_baseStackSource.CallFrameIndexLimit; }
        }
        public override int CallFrameIndexLimit
        {
            get { throw new NotImplementedException(); }
        }
        public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
        {
            return m_baseStackSource.GetSampleByIndex(sampleIndex);
        }
        public override int SampleIndexLimit { get { return m_baseStackSource.SampleIndexLimit; } }
        public override double SampleTimeRelMSecLimit { get { return m_baseStackSource.SampleTimeRelMSecLimit; } }

        #region private
        StackSource m_baseStackSource;
        string m_nodeName;
        #endregion
    }

    /// <summary>
    /// Creates a stack source that can refer to stacks from either the stack sources x or y.  This
    /// is useful to diffing or other scenarios where we wish to combine sources.  
    /// 
    /// TODO use InternStackSource instead?
    /// </summary>
    public class CombinedStackSource : StackSource
    {
        public CombinedStackSource(StackSourceStacks x, StackSourceStacks y)
        {
            m_x = x;
            m_y = y;
        }

        public StackSourceCallStackIndex ConvertIndex(StackSourceCallStackIndex baseIndex, StackSourceStacks baseSource)
        {
            Debug.Assert(baseSource == m_x || baseSource == m_y);
            if (baseSource == m_x)
                return (StackSourceCallStackIndex)((((int)baseIndex) << 1) + 1);
            else
                return (StackSourceCallStackIndex)((((int)baseIndex) << 1) + 0);
        }
        public StackSourceFrameIndex ConvertIndex(StackSourceFrameIndex baseIndex, StackSourceStacks baseSource)
        {
            Debug.Assert(baseSource == m_x || baseSource == m_y);
            if (baseSource == m_x)
                return (StackSourceFrameIndex)((((int)baseIndex) << 1) + 1);
            else
                return (StackSourceFrameIndex)((((int)baseIndex) << 1) + 0);
        }

        public override void ProduceSamples(Action<StackSourceSample> callback)
        {
            throw new NotImplementedException();
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            if (((int)callStackIndex & 1) != 0)
                return m_x.GetCallerIndex((StackSourceCallStackIndex)(((int)callStackIndex) >> 1));
            else
                return m_y.GetCallerIndex((StackSourceCallStackIndex)(((int)callStackIndex) >> 1));
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            if (((int)callStackIndex & 1) != 0)
                return m_x.GetFrameIndex((StackSourceCallStackIndex)(((int)callStackIndex) >> 1));
            else
                return m_y.GetFrameIndex((StackSourceCallStackIndex)(((int)callStackIndex) >> 1));
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
        {
            if (((int)frameIndex & 1) != 0)
                return m_x.GetFrameName((StackSourceFrameIndex)(((int)frameIndex) >> 1), verboseName);
            else
                return m_y.GetFrameName((StackSourceFrameIndex)(((int)frameIndex) >> 1), verboseName);
        }
        public override int CallStackIndexLimit
        {
            get { return 2 * Math.Max(m_x.CallFrameIndexLimit, m_y.CallStackIndexLimit); }
        }
        public override int CallFrameIndexLimit
        {
            get { return 2 * Math.Max(m_x.CallFrameIndexLimit, m_y.CallFrameIndexLimit); }
        }

        #region private
        StackSourceStacks m_x;
        StackSourceStacks m_y;
        #endregion
    }

    /// <summary>
    /// SampleInfos of a set of stackSource by eventToStack.  This represents the entire call tree.   You create an empty one in using
    /// the default constructor and use 'AddSample' to add stackSource to it.   You traverse it by 
    /// </summary>
    public class CallTree
    {
        /// <summary>
        /// Creates an empty call tree.  Only useful so you can have a valid 'placeholder' value when you 
        /// have no samples.  
        /// </summary>
        public CallTree()
        {
            m_root = new CallTreeNode("ROOT", StackSourceFrameIndex.Root, null, this);
        }

        // TODO FIX NOW remove?
#if false 
        // TODO untested. 
        /// <summary>
        /// Compute the difference between two call trees.  Effectively it is a node-by-node difference 
        /// Thus you can have negative weights on some nodes. 
        /// </summary>
        public static CallTree Diff(CallTree x, CallTree y)
        {
            var ret = new CallTree(null);
            var stackSource = new CombinedStackSource(x.StackSource, y.StackSource);
            ret.StackSource = stackSource;
            ret.Diff(x.m_root, y.m_root, null, stackSource);
            return ret;
        }
#endif 

        // TODO, not tested. 
        /// <summary>
        /// Computes an 'inverted' tree root at 'nodeName' that represents the callers of 'nodeName'
        /// Thus this tree is rooted at 'nodeName' and all its leaves are 'Root' nodes.  
        /// This overlaps functionality in the caller-callee view, however this one displays information
        /// more than one level away.  
        /// </summary>
        public CallTree Callers(string focusName)
        {
            var ret = new CallTree();
            AccumlateCallers(focusName, m_root, 0);
            return ret;
        }
        /// <summary>
        /// Walks all inclusive samples of 'nodeInCalleesTree' and adds any that are for the method 'focusName' 
        /// (inclusively) to the current tree.   numFocusSeen is the number of times 'focusName' was seen in
        /// the callers of 'treeNode'. 
        /// 
        /// TODO currently when there is recurisioin we show the deepest nodes.  
        /// </summary>
        private void AccumlateCallers(string focusName, CallTreeNode nodeInCalleesTree, int numFocusNodesSeen)
        {
            bool isFocus = (nodeInCalleesTree.Name == focusName);
            if (isFocus)
                numFocusNodesSeen++;
            if (nodeInCalleesTree.m_callees != null)
            {
                int saveRecursion = numFocusNodesSeen;
                for (int i = 0; i < nodeInCalleesTree.m_callees.Count; i++)
                {
                    CallTreeNode child = nodeInCalleesTree.m_callees[i];
                    AccumlateCallers(focusName, child, numFocusNodesSeen);
                }
            }

            if (numFocusNodesSeen > 0 && nodeInCalleesTree.m_samples.Count > 0)
            {
                CallTreeNode nodeInCallersTree = m_root;
                for (CallTreeNode caller = nodeInCalleesTree; caller != null; caller = caller.Caller)
                    nodeInCallersTree = nodeInCallersTree.FindCallee(caller.ID);

                // TODO this duplicates all the lists of samples.   Do we care? 
                var source = nodeInCalleesTree.CallTree.StackSource;
                for (int i = 0; i < nodeInCalleesTree.m_samples.Count; i++)
                {
                    var sample = source.GetSampleByIndex(nodeInCalleesTree.m_samples[i]);
                    AddSample(nodeInCallersTree, sample);
                }
            }
        }

        /// <summary>
        /// The number of buckets to divide time into for the histogram.  see code:CallTreeNodeBase.InclusiveMetricByTime
        /// Only has an effect before the stack source is assigned.  
        /// 
        /// By default this is 0, which means that code:CallTreeNodeBase.InclusiveMetricByTime returns null;
        /// </summary>
        public int TimeHistogramSize
        {
            get { return m_timeHistogramSize; }
            set
            {
                if (m_root.InclusiveCount != 0) throw new InvalidOperationException("Must set before adding samples");
                m_timeHistogramSize = value;
                m_root.m_inclusiveMetricByTime = new float[m_timeHistogramSize];
            }
        }
        public double TimeHistogramStartRelMSec { get; set; }
        public double TimeHistogramEndRelMSec { get; set; }
        public double TimeBucketDurationMSec
        {
            get
            {
                var interval = (TimeHistogramEndRelMSec - TimeHistogramStartRelMSec);
                return interval / TimeHistogramSize;
            }
        }
        public double TimeBucketStartMSec(int bucketIndex)
        {
            return TimeBucketDurationMSec * bucketIndex + TimeHistogramStartRelMSec;
        }

        public StackSource StackSource
        {
            get { return m_SampleInfo; }
            set
            {
                if (m_SampleInfo != null)
                    m_root = new CallTreeNode("ROOT", StackSourceFrameIndex.Root, null, this);
                m_SampleInfo = value;
                m_sumByID = null;

                m_frames = new FrameInfo[100];  // A temporary stack used during AddSample, This is just a guess as to a good size.  

                value.ProduceSamples(AddSample);
                // And the basis for forming the % is total metric of stackSource.  
                PercentageBasis = Root.InclusiveMetric;

                // By default sort by inclusive Metric
                SortInclusiveMetricDecending();
                m_frames = null;                // Frames not needed anymore.  
            }
        }
        public CallTreeNode Root { get { return m_root; } }
        public float PercentageBasis { get; set; }

        /// <summary>
        /// Cause each treeNode in the calltree to be sorted (accending) based on comparer
        /// </summary>
        public void Sort(Comparison<CallTreeNode> comparer)
        {
            m_root.SortAll(comparer);
        }
        /// <summary>
        /// Sorting by InclusiveMetric Decending is so common, provide a shortcut.  
        /// </summary>
        public void SortInclusiveMetricDecending()
        {
            Sort(delegate(CallTreeNode x, CallTreeNode y)
            {
                int ret = Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric));
                if (ret != 0)
                    return ret;
                // Sort by first sample time (assending) if the counts are the same.  
                return x.FirstTimeRelMSec.CompareTo(y.FirstTimeRelMSec);
            });
        }
        public void ToXml(TextWriter writer)
        {
            writer.WriteLine("<CallTree TotalMetric=\"{0:f1}\">", Root.InclusiveMetric);
            Root.ToXml(writer, "");
            writer.WriteLine("</CallTree>");
        }
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw);
            return sw.ToString();
        }
        // Get a callerSum-calleeSum treeNode for 'nodeName'
        public CallerCalleeNode CallerCallee(string nodeName)
        {
            return new CallerCalleeNode(nodeName, this);
        }
        /// <summary>
        /// Return a list of nodes that have statisicts rolled up by treeNode by ID.  It is not
        /// sorted by anything in particular.   Note that ID is not quite the same thing as the 
        /// name.  You can have two nodes that have different IDs but the same Name.  These 
        /// will show up as two distinct entries in the resulting list.  
        /// </summary>
        public IEnumerable<CallTreeNodeBase> ByID { get { return GetSumByID().Values; } }

        public List<CallTreeNodeBase> ByIDSortedExclusiveMetric()
        {
            var ret = new List<CallTreeNodeBase>(ByID);
            ret.Sort((x, y) => Math.Abs(y.ExclusiveMetric).CompareTo(Math.Abs(x.ExclusiveMetric)));
            return ret;
        }

        /// <summary>
        /// If there are any nodes that have strictly less than to 'minInclusiveMetric'
        /// then remove the node, placing its samples into its parent (thus the parent's
        /// exclusive metric goes up).  
        /// 
        /// If useWholeTraceMetric is true, nodes are only foled if their inclusive metric
        /// OVER THE WHOLE TRACE is less than 'minInclusiveMetric'.  If false, then a node
        /// is folded if THAT NODE has less than the 'minInclusiveMetric'  
        /// 
        /// Thus if 'useWholeTraceMetric' == false then after calling this routine no
        /// node will have less than minInclusiveMetric.  
        /// 
        /// </summary>
        public int FoldNodesUnder(float minInclusiveMetric, bool useWholeTraceMetric)
        {
            m_root.CheckClassInvarients();

            // If we filter by whole trace metric we need to cacluate the byID sums.  
            Dictionary<int, CallTreeNodeBase> sumByID = null;
            if (useWholeTraceMetric)
                sumByID = GetSumByID();

            int ret = m_root.FoldNodesUnder(minInclusiveMetric, sumByID);

            m_root.CheckClassInvarients();
            m_sumByID = null;   // Force a recalculation of the list by ID
            return ret;
        }

        #region private
        private CallTree(CallTreeNode root) { m_root = root; }

        private struct FrameInfo
        {
            public StackSourceFrameIndex frameIndex;
            public int numFolds;
        }

        struct TreeCacheEntry
        {
            public StackSourceCallStackIndex StackIndex;
            public CallTreeNode Tree;
        }

        const int StackInfoCacheSize = 128;          // Must be a power of 2
        TreeCacheEntry[] m_TreeForStack = new TreeCacheEntry[StackInfoCacheSize];
        private CallTreeNode FindTreeNode(StackSourceCallStackIndex stack)
        {
            // var str = m_SampleInfo.ToString(stack);

            // Is it in our cache?
            int hash = (((int)stack) & (StackInfoCacheSize - 1));
            var entry = m_TreeForStack[hash];
            if (entry.StackIndex == stack && entry.Tree != null)
                return entry.Tree;

            if (stack == StackSourceCallStackIndex.Invalid)
                return m_root;

            var callerIndex = m_SampleInfo.GetCallerIndex(stack);
            var callerNode = FindTreeNode(callerIndex);

            var frameIndex = m_SampleInfo.GetFrameIndex(stack);
            var retNode = callerNode.FindCallee(frameIndex);

            // Update the cache.
            entry.StackIndex = stack;
            entry.Tree = retNode;
            m_TreeForStack[hash] = entry;

            return retNode;
        }

        private void AddSample(StackSourceSample sample)
        {
            AddSample(FindTreeNode(sample.StackIndex), sample);
        }

        private void AddSample(CallTreeNode treeNode, StackSourceSample sample)
        {
            // Add the sample to this node. 
            treeNode.m_exclusiveCount++;
            treeNode.m_exclusiveMetric += sample.Metric;
            if (sample.SampleIndex != StackSourceSampleIndex.Invalid)
                treeNode.m_samples.Add(sample.SampleIndex);

            var stackIndex = sample.StackIndex;
            // And update all the inclusive times up the tree to the root (including this node)
            while (treeNode != null)
            {
                treeNode.m_inclusiveCount++;
                treeNode.m_inclusiveMetric += sample.Metric;
                if (m_timeHistogramSize > 0)
                {
                    double bucket = ((sample.TimeRelMSec - TimeHistogramStartRelMSec) * m_timeHistogramSize / (TimeHistogramEndRelMSec - TimeHistogramStartRelMSec));
                    if (0 <= bucket && bucket <= treeNode.m_inclusiveMetricByTime.Length)
                    {
                        // We allow the time interval to be INCLUSIVE, which means we have to insure the index is within range.  
                        int index = (int)bucket;
                        if (index == treeNode.m_inclusiveMetricByTime.Length)
                            index = treeNode.m_inclusiveMetricByTime.Length - 1;
                        treeNode.m_inclusiveMetricByTime[index] += sample.Metric;
                    }
                    else
                        Debug.Assert(TimeHistogramEndRelMSec == TimeHistogramStartRelMSec, "Time Interval out of range");
                }

                if (sample.TimeRelMSec < treeNode.m_firstTimeRelMSec)
                    treeNode.m_firstTimeRelMSec = sample.TimeRelMSec;
                if (sample.TimeRelMSec > treeNode.m_lastTimeRelMSec)
                    treeNode.m_lastTimeRelMSec = sample.TimeRelMSec;
                Debug.Assert(treeNode.m_firstTimeRelMSec <= treeNode.m_lastTimeRelMSec);

                if (stackIndex != StackSourceCallStackIndex.Invalid)
                {
                    var numFoldedNodes = m_SampleInfo.GetNumberOfFoldedFrames(sample.StackIndex);
                    if (numFoldedNodes > 0)
                    {
                        if (stackIndex == sample.StackIndex)
                            treeNode.m_exclusiveFoldedMetric++;
                        if (numFoldedNodes > treeNode.MaxFoldedFrames)
                            treeNode.MaxFoldedFrames = numFoldedNodes;
                        if (numFoldedNodes < treeNode.MinFoldedFrames)
                            treeNode.MinFoldedFrames = numFoldedNodes;
                    }
                    stackIndex = m_SampleInfo.GetCallerIndex(stackIndex);
                }
                else
                {
                    Debug.Assert(treeNode == m_root);
                }

                treeNode = treeNode.Caller;
            }
        }

        private Dictionary<int, CallTreeNodeBase> GetSumByID()
        {
            if (m_sumByID == null)
            {
                m_sumByID = new Dictionary<int, CallTreeNodeBase>();
                var callersOnStack = new Dictionary<int, CallTreeNodeBase>();       // This is just a set
                AccumulateSumByID(m_root, callersOnStack);
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
                byIDNode.m_isByIdNode = true;
                m_sumByID.Add((int)treeNode.m_id, byIDNode);
            }

            bool newOnStack = !callersOnStack.ContainsKey((int)treeNode.m_id);
            // Add in the tree treeNode's contribution
            byIDNode.CombineByIdSamples(treeNode, newOnStack);

            Debug.Assert(treeNode.m_nextSameId == null);
            treeNode.m_nextSameId = byIDNode.m_nextSameId;
            byIDNode.m_nextSameId = treeNode;
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
                // TODO FIX NOW remove?
#if false 
        /// <summary>
        /// Compute the difference between x and y nodes.  Either can be null which semantically a call tree node with no samples. 
        /// </summary>
        private CallTreeNode Diff(CallTreeNode x, CallTreeNode y, CallTreeNode parent, CombinedStackSource targetStackSource)
        {
            Debug.Assert(x == null || y == null || x.Name == y.Name);

            // Form the statisics for result node by starting with X if present. 
            CallTreeNode ret = null;
            List<CallTreeNode> xCallees = null;
            List<CallTreeNode> yCallees = null;
            if (x != null)
            {
                xCallees = x.m_callees;
                ret = new CallTreeNode(x.Name, targetStackSource.ConvertIndex(x.ID, x.CallTree.StackSource), parent, this);
                ret.m_exclusiveCount = x.m_exclusiveCount;
                ret.m_exclusiveMetric = x.m_exclusiveMetric;
                ret.m_inclusiveCount = x.m_inclusiveCount;
                ret.m_inclusiveMetric = x.m_inclusiveMetric;

                ret.m_firstTimeRelMSec = x.m_firstTimeRelMSec;
                ret.m_lastTimeRelMSec = x.m_lastTimeRelMSec;
                if (x.m_inclusiveMetricByTime != null && parent.m_inclusiveMetricByTime != null &&
                    x.m_inclusiveMetricByTime.Length == parent.m_inclusiveMetricByTime.Length)
                {
                    ret.m_inclusiveMetricByTime = new float[x.m_inclusiveMetricByTime.Length];
                    for (int i = 0; i < x.m_inclusiveMetricByTime.Length; i++)
                        ret.m_inclusiveMetricByTime[i] = x.m_inclusiveMetricByTime[i];
                }
            }
            // then subtrace the statisics for the Y node if present. 
            if (y != null)
            {
                yCallees = y.m_callees;
                if (ret == null)
                    ret = new CallTreeNode(y.Name, targetStackSource.ConvertIndex(y.ID, y.CallTree.StackSource), parent, this);
                ret.m_exclusiveCount -= y.m_exclusiveCount;
                ret.m_exclusiveMetric -= y.m_exclusiveMetric;
                ret.m_inclusiveCount -= y.m_inclusiveCount;
                ret.m_inclusiveMetric = y.m_inclusiveMetric;

                ret.m_firstTimeRelMSec = y.m_firstTimeRelMSec;
                ret.m_lastTimeRelMSec = y.m_lastTimeRelMSec;
                if (y.m_inclusiveMetricByTime != null && parent.m_inclusiveMetricByTime != null &&
                    y.m_inclusiveMetricByTime.Length == parent.m_inclusiveMetricByTime.Length)
                {
                    if (ret.m_inclusiveMetricByTime == null)
                        ret.m_inclusiveMetricByTime = new float[parent.m_inclusiveMetricByTime.Length];

                    for (int i = 0; i < y.m_inclusiveMetricByTime.Length; i++)
                        ret.m_inclusiveMetricByTime[i] = -y.m_inclusiveMetricByTime[i];
                }
            }

            // Finally do children of these nodes. 
            if (xCallees != null || yCallees != null)
            {
                ret.m_callees = new List<CallTreeNode>();
                List<CallTreeNode> leftInY = (yCallees != null) ? new List<CallTreeNode>(yCallees) : null;

                if (xCallees != null)
                {
                    foreach (var callee in xCallees)
                        ret.m_callees.Add(Diff(callee, GetMatchAndRemove(callee.Name, leftInY), ret, targetStackSource));
                }
                if (leftInY != null)
                {
                    foreach (var callee in leftInY)
                        ret.m_callees.Add(Diff(null, callee, ret, targetStackSource));
                }
            }

            // TODO m_samples.  Should keep both, but remember that Y's weights are negative.  
            return ret;
        }
        /// <summary>
        /// A helper that finds the child called 'name' in 'childrenLeft', and removes it from the list.  Returns null if 
        /// 'name' does not exist.  
        /// </summary>
        private CallTreeNode GetMatchAndRemove(string name, List<CallTreeNode> childrenLeft)
        {
            if (childrenLeft == null)
                return null;

            for (int i = 0; i < childrenLeft.Count; i++)
            {
                var y = childrenLeft[i];
                if (y != null && y.Name == name)
                {
                    childrenLeft.RemoveAt(i);
                    return y;
                }
            }
            return null;
        }
#endif

        internal StackSource m_SampleInfo;
        private CallTreeNode m_root;
        internal int m_timeHistogramSize;
        Dictionary<int, CallTreeNodeBase> m_sumByID;          // These nodes hold the rollup by Frame ID (name)
        private FrameInfo[] m_frames;                         // Used to invert the stack only used during 'AddSample' phase.  
        #endregion
    }

    /// <summary>
    /// The part of a CalltreeNode that is common to Caller-calleeSum and the Calltree view.  
    /// </summary>
    public class CallTreeNodeBase
    {
        public CallTreeNodeBase(CallTreeNodeBase template)
        {
            m_id = template.m_id;
            m_name = template.m_name;
            m_callTree = template.m_callTree;
            m_inclusiveMetric = template.m_inclusiveMetric;
            m_inclusiveCount = template.m_inclusiveCount;
            m_exclusiveMetric = template.m_exclusiveMetric;
            m_exclusiveCount = template.m_exclusiveCount;
            m_exclusiveFoldedMetric = template.m_exclusiveFoldedMetric;
            m_firstTimeRelMSec = template.m_firstTimeRelMSec;
            m_lastTimeRelMSec = template.m_lastTimeRelMSec;
            // m_samples left out intentionally
            // m_nextSameId
            // m_isByIdNode
            if (template.m_inclusiveMetricByTime != null)
            {
                m_inclusiveMetricByTime = new float[template.m_inclusiveMetricByTime.Length];
                Array.Copy(template.m_inclusiveMetricByTime, m_inclusiveMetricByTime, template.m_inclusiveMetricByTime.Length);
            }
        }

        public string Name { get { return m_name; } }
        /// <summary>
        /// Name also includes an indication of how many stack frames were folded into this node
        /// Sutible for display but not for programatic comparision.  
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (MaxFoldedFrames == 0 && m_exclusiveFoldedMetric == 0)
                    return Name;

                return Name;
                /* TODO FIX NOW decide what to do here. 
                StringBuilder sb = new StringBuilder();
                sb.Append(Name).Append("   {");
                if (m_exclusiveFoldedMetric != 0)
                    sb.Append(ExclusiveFoldedPercent.ToString("f1")).Append("% ExcFromFold");
                if (MaxFoldedFrames != 0)
                {
                    if (m_exclusiveFoldedMetric != 0)
                        sb.Append(' ');
                    sb.Append("depth ").Append(MinFoldedFrames + 1).Append('-').Append(MaxFoldedFrames + 1);
                }
                sb.Append('}');
                return sb.ToString();
                 ***/
            }
        }
        /// <summary>
        /// The ID represents a most fine grained uniqueness associated with this node.   Typically it represents
        /// a particular method (however it is possible that two methods can have the same name (because the scope
        /// was not caputured).   Thus there can be multiple nodes with the same Name but different IDs.   
        /// 
        /// This can be StackSourceFrameIndex.Invalid for Caller-callee nodes (which have names, but no useful ID.  
        ///
        /// If ID != Invalid, and the IDs are the same then the names need to be the same.  
        /// </summary>
        public StackSourceFrameIndex ID { get { return m_id; } }
        public float InclusiveMetric { get { return m_inclusiveMetric; } }
        public float ExclusiveMetric { get { return m_exclusiveMetric; } }
        public float InclusiveCount { get { return m_inclusiveCount; } }
        public float ExclusiveCount { get { return m_exclusiveCount; } }
        public float InclusiveMetricPercent { get { return m_inclusiveMetric * 100 / m_callTree.PercentageBasis; } }
        public float ExclusiveMetricPercent { get { return m_exclusiveMetric * 100 / m_callTree.PercentageBasis; } }
        /// <summary>
        /// This is the exclusive metric that was added because nodes where folded into this node.  
        /// </summary>
        public float ExclusiveFoldedMetric { get { return m_exclusiveFoldedMetric; } }
        public float ExclusiveFoldedPercent { get { return m_exclusiveFoldedMetric * 100 / m_exclusiveMetric; } }

        public double FirstTimeRelMSec { get { return m_firstTimeRelMSec; } }
        public double LastTimeRelMSec { get { return m_lastTimeRelMSec; } }
        public double DurationMSec { get { return m_lastTimeRelMSec - m_firstTimeRelMSec; } }
        /// <summary>
        /// The call tree that contains this node.  
        /// </summary>
        public CallTree CallTree { get { return m_callTree; } }
        /// <summary>
        /// Time is broken up into N buckets and a imetric is summed into these buckets
        /// 
        /// Returns null unless code:CallTree.TimeHistogramSize is set to some positive number
        /// 
        /// The contact is that this is READ ONLY
        /// </summary>
        public float[] InclusiveMetricByTime { get { return m_inclusiveMetricByTime; } }
        /// <summary>
        /// This is a string that respresents the buckets of samples InclusiveMetricByTime
        /// </summary>
        public string InclusiveMetricByTimeString
        {
            get
            {
                if (m_inclusiveMetricByTime == null)
                    return "";
                var chars = new char[m_inclusiveMetricByTime.Length];
                double interval = (CallTree.TimeHistogramEndRelMSec - CallTree.TimeHistogramStartRelMSec) / CallTree.TimeHistogramSize;
                for (int i = 0; i < m_inclusiveMetricByTime.Length; i++)
                {
                    float metric = m_inclusiveMetricByTime[i];
                    char val = '_';
                    if (metric > 0)
                    {
                        // If we are consuming 100% of one CPU then we expect metricPerMSec to be 1 since samples happen every msec
                        double metricPerMsec = metric / interval;
                        int valueBucket = (int)(metricPerMsec * 10);       // TODO should we round?
                        if (valueBucket < 10)
                            val = (char)('0' + valueBucket);
                        else
                        {
                            valueBucket -= 10;
                            if (valueBucket < 25)
                                val = (char)('A' + valueBucket);          // We go through the alphabet too.
                            else
                                val = '*';                                // Greater than 3.6X CPUs 
                        }
                    }
                    chars[i] = val;
                }
                return new string(chars);
            }
            set { } // TODO See if there is a better way of getting the GUI working.  
        }

        // TODO review the aggregation of this. 
        /// <summary>
        /// If this node represents more than one call frame, this is the Maximum count of nodes
        /// folded into this node.  
        /// </summary>
        public int MaxFoldedFrames { get; internal set; }
        /// <summary>
        /// If this node represents more than one call frame, this is the Minimum count of nodes
        /// folded into this node.  
        /// </summary>       
        public int MinFoldedFrames { get; internal set; }

        /// <summary>
        /// Return a StackSource that has all the samples in this node.  If exclusive==true then just he
        /// sample exclusively in this node are returned, otherwise it is the inclusive samples.   
        /// 
        /// If the original stack source that was used to create this CodeTreeNode was a FilterStackSource
        /// then that filtering is removed in the returned StackSource.  
        /// </summary>
        public StackSource GetUngroupedSamples(bool exclusive = false)
        {
            return new CallTreeNodeStackSource(this, exclusive);
        }

        public void ToXmlAttribs(TextWriter writer)
        {
            writer.Write(" Name=\"{0}\"", XmlUtilities.XmlEscape(Name, false));
            writer.Write(" ID=\"{0}\"", (int)m_id);
            writer.Write(" InclusiveMetric=\"{0}\"", InclusiveMetric);
            writer.Write(" ExclusiveMetric=\"{0}\"", ExclusiveMetric);
            writer.Write(" InclusiveCount=\"{0}\"", InclusiveCount);
            writer.Write(" ExclusiveCount=\"{0}\"", ExclusiveCount);
            writer.Write(" FirstTimeRelMSec=\"{0:f4}\"", FirstTimeRelMSec);
            writer.Write(" LastTimeRelMSec=\"{0:f4}\"", LastTimeRelMSec);
            if (m_samples.Count != 0)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(" Samples = \"");
                for (int i = 0; i < m_samples.Count; i++)
                    sb.Append(' ').Append((int)m_samples[i]);
                sb.Append("\"");
                writer.Write(sb.ToString());
            }
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
            this.m_callTree = container;
            this.m_id = id;
            this.m_firstTimeRelMSec = Double.PositiveInfinity;
            this.m_lastTimeRelMSec = Double.NegativeInfinity;
            this.MinFoldedFrames = int.MaxValue;
            if (container.m_timeHistogramSize > 0)
                this.m_inclusiveMetricByTime = new float[container.m_timeHistogramSize];
        }

        // TODO should we just use a CopyStackSource instead?  
        /// <summary>
        /// Private StackSource implementation that the code:CallTreeNodeBase.Samples method returns.  This basically
        /// just returns a stack source filtered to just those nodes.  
        /// </summary>
        private class CallTreeNodeStackSource : StackSource
        {
            public CallTreeNodeStackSource(CallTreeNodeBase node, bool exclusive)
            {
                // Set things that never change as we iterate

                // TODO: This is clunky and has too much policy wired into it.  
                // Unwrap any filters. 
                m_source = node.CallTree.StackSource.BaseStackSource;
                while (m_source != node.CallTree.StackSource.BaseStackSource)
                    m_source = node.CallTree.StackSource.BaseStackSource;

                m_node = node;              //  m_node never changes (what was passed in)
                m_inclusive = !exclusive;
#if DEBUG
                // Set initialization variables. 
                Reset();
                CheckSamples();
#endif
                Reset();
            }

            public override void ProduceSamples(Action<StackSourceSample> callback)
            {
                Reset();
                for (; ; )
                {
                    var sample = GetNextSample();
                    if (sample == null)
                        break;
                    callback(sample);
                }
            }

            /// <summary>
            /// Resets the enumeration that GetNextSample goes through.  
            /// </summary>
            // TODO remove dependency in ProduceSample and remove         
            private void Reset()
            {
                // if we are enumerating byId, m_node is the current node in the linked list
                m_curByIdNode = null;
                if (m_node.m_isByIdNode)        // Does this node represent all treeNodes with the same ID?
                    m_curByIdNode = m_node.m_nextSameId;
                SetCurNode(m_node);
            }

#if DEBUG
            [Conditional("DEBUG")]
            private void CheckSamples()
            {
                int i = 0;
                while (GetNextSample() != null)
                    i++;

                if (m_inclusive)
                    Debug.Assert(m_node.m_inclusiveCount == i);
                else
                    Debug.Assert(m_node.m_exclusiveCount == i);
            }
#endif
            private void SetCurNode(CallTreeNodeBase newValue)
            {
                m_curNode = newValue;           // This is the node within a tree enumeration
                m_curIdx = 0;
                if (m_inclusive)
                {
                    var treeNode = newValue as CallTreeNode;
                    if (treeNode != null && treeNode.Callees != null)
                    {
                        foreach (var callee in treeNode.Callees)
                        {
                            // To avoid double-counting for the inclusive case, we ignore inclusive nodes
                            // that are the same ID (since they are counted already)
                            if (!m_node.m_isByIdNode || callee.ID != m_node.ID)
                                m_curNodesToEnumerate.Add(callee);
                        }
                    }
                }
            }

            // TODO remove dependency in ProduceSample and remove         
            private StackSourceSample GetNextSample()
            {
                // Have we run out on the current tree node?
                while (m_curIdx >= m_curNode.m_samples.Count)
                {
                    // Have we run out of the tree walking nodes?
                    if (m_curNodesToEnumerate.Count == 0)
                    {
                        // Have we run out of nodes with the same ID (name)? 
                        if (m_curByIdNode == null)
                            return null;            // We are done (Yeah!)

                        // Go on to the next name of the same ID.  
                        SetCurNode(m_curByIdNode);
                        m_curByIdNode = m_curByIdNode.m_nextSameId;
                    }
                    else
                    {
                        // Pop off node off the list
                        SetCurNode(m_curNodesToEnumerate.Pop());
                    }
                }
                return m_source.GetSampleByIndex(m_curNode.m_samples[m_curIdx++]);
            }
            public override StackSourceSample GetSampleByIndex(StackSourceSampleIndex sampleIndex)
            {
                return m_source.GetSampleByIndex(sampleIndex);
            }
            public override int SampleIndexLimit { get { return m_source.SampleIndexLimit; } }
            public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
            {
                return m_source.GetCallerIndex(callStackIndex);
            }
            public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
            {
                return m_source.GetFrameIndex(callStackIndex);
            }
            public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
            {
                return m_source.GetFrameName(frameIndex, verboseName);
            }
            public override int CallStackIndexLimit { get { return m_source.CallStackIndexLimit; } }
            public override int CallFrameIndexLimit { get { return m_source.CallFrameIndexLimit; } }
            public override StackSource BaseStackSource { get { return m_source; } }

            #region private
            // These point at the current state of iteration (all begin with m_cur*  Iteration is in 4 levels
            //  * m_curByIdNode - The current node in the linked list of nodes with the same name (if 
            //      we are enumerating a byName node).  This points at the REST of the nodes (not the node we are 
            //      currently working on);
            //  * m_curNodesToEnumerate - The stack of nodes we had yet to enumerate in an inclusive enumeration of the tree
            //  * m_curNode - The current node we are enumerating exclusive samples (m_samples)
            //  * m_curIdx - the index within the m_samples array
            CallTreeNodeBase m_curByIdNode;
            GrowableArray<CallTreeNode> m_curNodesToEnumerate;  // List of all nodes we still need to look at m_samples.  
            CallTreeNodeBase m_curNode;             // Points to code whose m_samples we are walking
            int m_curIdx;                           // Current position in m_samples

            // These don't change while we iterate 
            bool m_inclusive;
            CallTreeNodeBase m_node;
            StackSource m_source;
            #endregion
        }

        /// <summary>
        /// Combines the 'this' node with 'otherNode'.   If 'newOnStack' is true, then the inclusive
        /// metrics are also updated.  
        /// 
        /// Note that I DON'T accumlate other.m_samples into this.m_samples.   This is because this routine is
        /// only intended to be called from AccumlateByID and the intent there is that m_nextSameId already handles
        /// that.  
        /// </summary>
        internal void CombineByIdSamples(CallTreeNodeBase other, bool addInclusive, double weight = 1)
        {
            // TODO
            if (addInclusive)
            {
                m_inclusiveMetric += (float)(other.m_inclusiveMetric * weight);
                m_inclusiveCount += (float)(other.m_inclusiveCount * weight);
                if (m_inclusiveMetricByTime != null && other.m_inclusiveMetricByTime != null)
                {
                    for (int i = 0; i < m_inclusiveMetricByTime.Length; i++)
                        m_inclusiveMetricByTime[i] += (float)(other.m_inclusiveMetricByTime[i] * weight);
                }
            }
            m_exclusiveMetric += (float)(other.m_exclusiveMetric * weight);
            m_exclusiveCount += (float)(other.m_exclusiveCount * weight);
            m_exclusiveFoldedMetric += (float)(other.m_exclusiveFoldedMetric * weight);

            if (other.MinFoldedFrames < MinFoldedFrames)
                MinFoldedFrames = other.MinFoldedFrames;
            if (MaxFoldedFrames < other.MaxFoldedFrames)
                MaxFoldedFrames = other.MaxFoldedFrames;

            if (other.m_firstTimeRelMSec < m_firstTimeRelMSec)
                m_firstTimeRelMSec = other.m_firstTimeRelMSec;
            if (other.m_lastTimeRelMSec > m_lastTimeRelMSec)
                m_lastTimeRelMSec = other.m_lastTimeRelMSec;

            if (m_firstTimeRelMSec > m_lastTimeRelMSec)
                Debug.WriteLine("Error First > Last");
        }

        internal StackSourceFrameIndex m_id;
        internal readonly string m_name;
        internal readonly CallTree m_callTree;
        internal float m_inclusiveMetric;
        internal float m_inclusiveCount;
        internal float m_exclusiveMetric;
        internal float m_exclusiveCount;
        internal float m_exclusiveFoldedMetric;
        internal double m_firstTimeRelMSec;
        internal double m_lastTimeRelMSec;

        internal GrowableArray<StackSourceSampleIndex> m_samples;       // The actual samples.  
        internal float[] m_inclusiveMetricByTime;                       // histogram by time. 
        internal CallTreeNodeBase m_nextSameId;                         // We keep a linked list of tree nodes with the same ID (name)
        internal bool m_isByIdNode;                                     // Is this a node representing a rollup by ID (name)?  
        #endregion
    }

    /// <summary>
    /// Represents a single treeNode in a code:CallTree 
    /// 
    /// Each node keeps all the sample with the same path to the root.  
    /// Each node also remembers its parent (caller) and children (callees).
    /// The nodes also keeps the IDs of all its samples (so no information
    /// is lost, just sorted by stack).   You get at this through the
    /// code:CallTreeNodeBase.GetUngroupedSamples method.  
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

        public void ToXml(TextWriter writer, string indent)
        {

            writer.Write("{0}<CallTree ", indent);
            this.ToXmlAttribs(writer);
            writer.WriteLine(">");

            var childIndent = indent + " ";
            if (m_callees != null)
            {
                foreach (CallTreeNode callee in m_callees)
                {
                    callee.ToXml(writer, childIndent);
                }
            }
            writer.WriteLine("{0}</CallTree>", indent);
        }

        /// <summary>
        /// Adds up the counts of all 'Broken' nodes in a particular tree node
        /// </summary>
        public float GetBrokenStackCount(int depth = 4)
        {
            if (depth <= 0)
                return 0;

            if (this.Name == "BROKEN")          // TODO use ID instead
                return this.InclusiveCount;

            float ret = 0;
            if (this.Callees != null)
                foreach (var child in this.Callees)
                    ret += child.GetBrokenStackCount(depth - 1);

            return ret;
        }

        /// <summary>
        /// Creates a string that has spaces | and + signs that represent the indentation level 
        /// for the tree node.  (Called from XAML)
        /// </summary>
        public string IndentString
        {
            get
            {
                if (m_indentString == null)
                {
                    var depth = Depth();
                    var chars = new char[depth];
                    var i = depth - 1;
                    if (0 <= i)
                    {
                        chars[i] = '+';
                        var ancestor = Caller;
                        --i;
                        while (i >= 0)
                        {
                            chars[i] = ancestor.IsLastChild() ? ' ' : '|';
                            ancestor = ancestor.Caller;
                            --i;
                        }
                    }

                    m_indentString = new string(chars);
                }
                return m_indentString;
            }
        }
        public override string ToString()
        {
            StringWriter sw = new StringWriter();
            ToXml(sw, "");
            return sw.ToString();
        }
        #region private
        public CallTreeNode(string name, StackSourceFrameIndex id, CallTreeNode caller, CallTree container)
            : base(name, id, container)
        {
            this.m_caller = caller;
        }

        /// <summary>
        /// Fold away any nodes having less than 'minInclusiveMetric'.  If 'sumByID' is non-null then the 
        /// only nodes that have a less then the minInclusiveMetric for the whole trace are folded. 
        /// </summary>
        internal int FoldNodesUnder(float minInclusiveMetric, Dictionary<int, CallTreeNodeBase> sumByID)
        {
            int nodesFolded = 0;
            if (m_callees != null)
            {
                int to = 0;
                for (int from = 0; from < m_callees.Count; from++)
                {
                    var callee = m_callees[from];
                    // We don't fold away Broken stacks ever.  
                    if (Math.Abs(callee.InclusiveMetric) < minInclusiveMetric && callee.m_id != StackSourceFrameIndex.Broken &&
                        (sumByID == null || sumByID[(int) callee.m_id].InclusiveMetric < minInclusiveMetric))
                    {
                        // TODO the samples are no longer in time order, do we care?
                        nodesFolded++;
                        m_exclusiveCount += callee.m_inclusiveCount;
                        m_exclusiveMetric += callee.m_inclusiveMetric;
                        m_exclusiveFoldedMetric += callee.m_inclusiveMetric;
                        var newMin = callee.MinFoldedFrames + 1;
                        if (newMin < MinFoldedFrames && callee.MinFoldedFrames < MinFoldedFrames)   // second condition avoids wrap around
                            MinFoldedFrames = newMin;
                        var newMax = callee.MaxFoldedFrames + 1;
                        if (MaxFoldedFrames < newMax)
                            MaxFoldedFrames = newMax;

                        // Transfer the samples to the caller 
                        TransferInclusiveSamplesToList(callee, ref m_samples);
                    }
                    else
                    {
                        nodesFolded += callee.FoldNodesUnder(minInclusiveMetric, sumByID);
                        if (to != from)
                            m_callees[to] = m_callees[from];
                        to++;
                    }
                }

                if (to == 0)
                    m_callees = null;
                else if (to != m_callees.Count)
                    m_callees.RemoveRange(to, m_callees.Count - to);
                Debug.Assert((to == 0 && m_callees == null) || to == m_callees.Count);
            }

            // TODO is it worth putting this back (taking into account the 'Broken' node case?
            //Debug.Assert(this == m_callTree.Top || InclusiveMetric >= minInclusiveMetric);
            // TODO FIX NOW Debug.Assert(Math.Abs(InclusiveMetric - ExclusiveMetric) >= -Math.Abs(InclusiveMetric) * .001);
            // TODO FIX NOW Debug.Assert(m_callees != null || Math.Abs(ExclusiveMetric - InclusiveMetric) <= .001 * Math.Abs(ExclusiveMetric));
            return nodesFolded;
        }

        // Transfer all samples (inclusively from 'fromNode' to 'toList'.  
        private static void TransferInclusiveSamplesToList(CallTreeNode fromNode, ref GrowableArray<StackSourceSampleIndex> toList)
        {
            // Transfer the exclusive samples.
            for (int i = 0; i < fromNode.m_samples.Count; i++)
                toList.Add(fromNode.m_samples[i]);

            // And now all the samples from children
            if (fromNode.m_callees != null)
            {
                for (int i = 0; i < fromNode.m_callees.Count; i++)
                    TransferInclusiveSamplesToList(fromNode.m_callees[i], ref toList);
            }
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
                    if (callee.Name == m_callTree.m_SampleInfo.GetFrameName(frameID, false))
                        Debug.WriteLine(string.Format("Warning Got frame ID {0} Name {1} == {2} Name {3}", callee.m_id, callee.Name, frameID, m_callTree.m_SampleInfo.GetFrameName(frameID, false)));
#endif
                }
            }
            else
                m_callees = new List<CallTreeNode>();

            string frameName = m_callTree.m_SampleInfo.GetFrameName(frameID, false);
            Debug.Assert(frameName != null);

            callee = new CallTreeNode(frameName, frameID, this, m_callTree);
            m_callees.Add(callee);
            return callee;
        }

        private bool IsLastChild()
        {
            var parentCallees = Caller.Callees;
            return (parentCallees[parentCallees.Count - 1] == this);
        }

        private int Depth()
        {
            int ret = 0;
            CallTreeNode ptr = Caller;
            while (ptr != null)
            {
                ret++;
                ptr = ptr.Caller;
            }
            return ret;
        }

        [Conditional("DEBUG")]
        public void CheckClassInvarients()
        {
            // m_samples can be 0 if the stack source does not support sample indexes.  
            Debug.Assert(m_exclusiveCount == m_samples.Count || m_samples.Count == 0);

            float sum = m_exclusiveMetric;
            float count = m_exclusiveCount;
            if (m_callees != null)
            {
                for (int i = 0; i < m_callees.Count; i++)
                {
                    var callee = m_callees[i];
                    callee.CheckClassInvarients();
                    sum += callee.m_inclusiveMetric;
                    count += callee.m_inclusiveCount;
                }
            }
            Debug.Assert(Math.Abs(sum - m_inclusiveMetric) <= Math.Abs(sum) * .001);
            Debug.Assert(count == m_inclusiveCount);
        }

        // state;
        private readonly CallTreeNode m_caller;
        internal List<CallTreeNode> m_callees;
        private string m_indentString;
        #endregion
    }

    /// <summary>
    /// A code:CallerCalleeNode gives statistics that focus on a NAME.  (unlike calltrees that use ID)
    /// It takes all stackSource that have callStacks that include that treeNode and compute the metrics for
    /// all the callers and all the callees for that treeNode.  
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
#if false   // FIX NOW 
            float totalMetric;
            float totalCount;
            AccumlateSamplesForNode(callTree.Root, 0, out totalMetric, out totalCount);

            Debug.Assert(totalCount <= callTree.Root.InclusiveCount);
            Debug.Assert(totalMetric <= callTree.Root.InclusiveMetric);
#else
            CallTreeNodeBase weightedSummary;
            double weightedSummaryScale;
            bool isUniform;
            AccumlateSamplesForNode(callTree.Root, 0, out weightedSummary, out weightedSummaryScale, out isUniform);
#endif

            m_callers.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));
            m_callees.Sort((x, y) => Math.Abs(y.InclusiveMetric).CompareTo(Math.Abs(x.InclusiveMetric)));

#if DEBUG
            float callerSum = 0;
            foreach (var caller in m_callers)
                callerSum += caller.m_inclusiveMetric;

            float calleeSum = 0;
            foreach (var callee in m_callees)
                calleeSum += callee.m_inclusiveMetric;

            if (this.Name != m_callTree.Root.Name)
                Debug.Assert(Math.Abs(callerSum - m_inclusiveMetric) <= .001);
            Debug.Assert(Math.Abs(calleeSum + m_exclusiveMetric - m_inclusiveMetric) <= .001 * Math.Abs(m_inclusiveMetric));
#endif
        }

        public IList<CallTreeNodeBase> Callers { get { return m_callers; } }
        public IList<CallTreeNodeBase> Callees { get { return m_callees; } }

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
        /// A caller callee view is a sumation which centers around one 'focus' node which is represented by the CallerCalleeNode.
        /// This node has a caller and callee list, an these nodes (as well as the CallerCalleNode itself) represent the aggregation
        /// over the entire tree.
        /// 
        /// AccumlateSamplesForNode is the routine that takes a part of a aggregated call tree (repsesented by 'treeNode' and adds
        /// in the statistics for that call tree into the CallerCalleeNode aggregations (and its caller and callee lists).  
        /// 
        /// 'recursionsCount' is the number of times the focus node name has occured in the path from 'treeNode' to the root.   In 
        /// addition to setting the CallerCalleeNode aggregation, it also returns a 'weightedSummary' inclusive aggregation 
        /// FOR JUST treeNode (the CallerCalleNode is an aggregation over the entire call tree accumulated so far).  
        /// 
        /// The key problem for this routine to avoid is double counting of inclusive samples in the face of recursive functions. 
        /// Thus all samples are weighted by the recurision count before being included in 'weightedSummaryRet (as well as in
        /// the CallerCalleeNode and its Callers and Callees).    
        /// 
        /// An important optimization is the ability to NOT create (but rather reuse) CallTreeNodes when returning weightedSummaryRet.
        /// To accompish this the summaryWeightRet is needed.  To get the correct numerical value for weightedSummaryRet, you actually
        /// have to scale values by weightedSummaryScaleRet before use.   This allows us to represent weights of 0 (subtree has no
        /// calls to the focus node), or cases where the subtree is completely uniform in its weigthing (the subtree does not contain
        /// any additional focus nodes), by simply returning the tree node itself and scaling it by the recurision count).  
        /// 
        /// isUniformRet is set to true if anyplace in 'treeNode' does not have the scaling factor weightedSummaryScaleRet.  This
        /// means the the caller cannot simply scale 'treeNode' by a weight to get weightedSummaryRet.  
        /// </summary>
        private void AccumlateSamplesForNode(CallTreeNode treeNode, int recursionCount,
            out CallTreeNodeBase weightedSummaryRet, out double weightedSummaryScaleRet, out bool isUniformRet)
        {
            bool isFocusNode = treeNode.Name.Equals(Name);
            if (isFocusNode)
                recursionCount++;

            // We hope we are uniform (will fix if this is not true)
            isUniformRet = true;

            // Compute the weighting.   This is either 0 if we have not yet seen the focus node, or
            // 1/recusionCount if we have (splitting all samples equally among each of the samples)
            weightedSummaryScaleRet = 0;
            weightedSummaryRet = null;          // If the weight is zero, we don't care about the value
            if (recursionCount > 0)
            {
                weightedSummaryScaleRet = 1.0F / recursionCount;

                // We oportunistically hope that all nodes in this subtree have the same weighting and thus
                // we can simply return the treeNode itself as the summary node for this subtree.  
                // This will get corrected to the proper value if our hopes prove unfounded.  
                weightedSummaryRet = treeNode;
            }

            // Get all the samples for the children and set the calleeSum information  We also set the
            // information in the CallerCalleNode's Callees list.  
            if (treeNode.m_callees != null)
            {
                for (int i = 0; i < treeNode.m_callees.Count; i++)
                {
                    CallTreeNode treeNodeCallee = treeNode.m_callees[i];

                    // Get the correct weighted summary for the children.  
                    CallTreeNodeBase calleeWeightedSummary;
                    double calleeWeightedSummaryScale;
                    bool isUniform;
                    AccumlateSamplesForNode(treeNodeCallee, recursionCount, out calleeWeightedSummary, out calleeWeightedSummaryScale, out isUniform);

                    // Did we have any samples at all that contained the focus node this treeNode's callee?
                    if (weightedSummaryScaleRet != 0 && calleeWeightedSummaryScale != 0)
                    {
                        // Yes, then add the summary for the treeNode's callee to cooresponding callee node in 
                        // the caller-callee aggregation. 
                        if (isFocusNode)
                        {
                            var callee = Find(ref m_callees, treeNodeCallee.Name);
                            callee.CombineByIdSamples(calleeWeightedSummary, true, calleeWeightedSummaryScale);
                        }

                        // And also add it to the weightedSummaryRet node we need to return.   
                        // This is the trickiest part of this code.  The way this works is that
                        // return value ALWAYS starts with the aggregation AS IF the weighting
                        // was uniform.   However if that proves to be an incorrect assumption
                        // we subtract out the uniform values and add back in the correctly weighted 
                        // values.   
                        if (!isUniform || calleeWeightedSummaryScale != weightedSummaryScaleRet)
                        {
                            isUniformRet = false;       // We ourselves are not uniform.  

                            // We can no longer use the optimization of using the treenode itself as our weighted
                            // summary node because we need to write to it.   Thus replace the node with a copy.  
                            if (weightedSummaryRet == treeNode)
                                weightedSummaryRet = new CallTreeNodeBase(weightedSummaryRet);

                            // Subtract out the unweighted value and add in the weighted one
                            double scale = calleeWeightedSummaryScale / weightedSummaryScaleRet;
                            weightedSummaryRet.m_inclusiveMetric += (float)(calleeWeightedSummary.m_inclusiveMetric * scale - treeNodeCallee.m_inclusiveMetric);
                            weightedSummaryRet.m_inclusiveCount += (float)(calleeWeightedSummary.m_inclusiveCount * scale - treeNodeCallee.m_inclusiveCount);
                            if (weightedSummaryRet.m_inclusiveMetricByTime != null)
                            {
                                for (int j = 0; j < m_inclusiveMetricByTime.Length; j++)
                                    weightedSummaryRet.m_inclusiveMetricByTime[j] +=
                                        (float)(calleeWeightedSummary.m_inclusiveMetricByTime[j] * scale - treeNodeCallee.m_inclusiveMetricByTime[j]);
                            }
                        }
                    }
                }
            }

            // OK we are past the tricky part of creating a weighted summary node.   If this is a focus node, we can simply
            // Add this aggregation to the CallerCallee node itself as well as the proper Caller node.  
            if (isFocusNode)
            {
                this.CombineByIdSamples(weightedSummaryRet, true, weightedSummaryScaleRet);

                // Set the Caller information now 
                CallTreeNode callerTreeNode = treeNode.Caller;
                if (callerTreeNode != null)
                    Find(ref m_callers, callerTreeNode.Name).CombineByIdSamples(weightedSummaryRet, true, weightedSummaryScaleRet);
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
            elem = new CallTreeNodeBase(frameName, StackSourceFrameIndex.Invalid, m_callTree);
            elems.Add(elem);
            return elem;
        }

        // state;
        private List<CallTreeNodeBase> m_callers;
        private List<CallTreeNodeBase> m_callees;
        #endregion
    }
}
