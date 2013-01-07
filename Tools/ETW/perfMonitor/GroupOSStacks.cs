// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Diagnostics.Tracing.StackSources;

namespace Stacks
{
    /// <summary>
    /// This stack source will group together any frames that come from the OS (anything in a module under \Windows),
    /// until it leaves the OS.   
    /// 
    /// This is sort of like the 'just my code' feature in the VS profiler, except it is 'no os code' instead.
    /// </summary>
    public class OSGroupingStackSource : StackSource
    {
        public OSGroupingStackSource(StackSource stackSource)
        {
            m_baseStackSource = stackSource;

            // Intialize the StackInfo cache (and the IncPathsMatchedSoFarStorage variable)
            m_stackInfoCache = new StackInfo[StackInfoCacheSize];
            for (int i = 0; i < m_stackInfoCache.Length; i++)
                m_stackInfoCache[i] = new StackInfo();
        }
        public override void ProduceSamples(Action<StackSourceSample> callback)
        {
            m_baseStackSource.ProduceSamples(delegate(StackSourceSample sample)
            {
                callback(sample);
            });
        }
        public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
        {
            StackInfo stackInfo = GetStackInfo(callStackIndex);
            return stackInfo.CallerIndex;
        }
        public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
        {
            StackInfo stackInfo = GetStackInfo(callStackIndex);
            return stackInfo.FrameIndex;
        }
        public override string GetFrameName(StackSourceFrameIndex frameIndex, bool fullName)
        { return m_baseStackSource.GetFrameName(frameIndex, fullName); }
        public override int CallStackIndexLimit { get { return m_baseStackSource.CallStackIndexLimit; } }
        public override int CallFrameIndexLimit { get { return m_baseStackSource.CallFrameIndexLimit; } }
        public override StackSource BaseStackSource { get { return m_baseStackSource; } }

        #region private
        /// <summary>
        /// Associated with every frame is a FrameInfo which is the computed answers associated with that frame name.  
        /// We cache these and so most of the time looking up frame information is just an array lookup.  
        /// 
        /// FrameInfo contains information that is ONLY dependent on the frame name (not the stack it came from), so
        /// entry point groups and include patterns can not be completely processed at this point.   Never returns null. 
        /// </summary>
        private StackInfo GetStackInfo(StackSourceCallStackIndex stackIndex)
        {
            Debug.Assert(0 <= stackIndex);                              // No illegal stacks, or other special stacks.  
            Debug.Assert((int)stackIndex < CallStackIndexLimit);         // And in range.  

            // Check the the cache, otherwise create it.  
            int hash = (((int)stackIndex) & (StackInfoCacheSize - 1));
            var stackInfo = m_stackInfoCache[hash];
            if (stackInfo.StackIndex != stackIndex)
            {
                // Try to reuse the slot.  Give up an allocate if necessary (TODO we can recycle if it happens frequently)
                if (stackInfo.InUse)
                    stackInfo = new StackInfo();
                stackInfo.InUse = true;
                GenerateStackInfo(stackIndex, stackInfo);
                stackInfo.InUse = false;
            }
            return stackInfo;
        }
        /// <summary>
        /// Generate the stack information for 'stack' and place it in stackInfoRet.  Only called by GetStackInfo.    
        /// </summary>
        private void GenerateStackInfo(StackSourceCallStackIndex stackIndex, StackInfo stackInfoRet)
        {
            // Clear out old information.  
            stackInfoRet.StackIndex = stackIndex;
            stackInfoRet.CallerIndex = m_baseStackSource.GetCallerIndex(stackIndex);

            // Get the frame information 
            var ungroupedFrameIndex = m_baseStackSource.GetFrameIndex(stackIndex);
            // By default we use the ungrouped frame index as our frame index (and grouping will modify this). 
            stackInfoRet.FrameIndex = ungroupedFrameIndex;
            string fullName = m_baseStackSource.GetFrameName(ungroupedFrameIndex, true);

            // Am I a OS function?
            stackInfoRet.IsOSMethod = fullName.IndexOf(@"\Windows\", StringComparison.OrdinalIgnoreCase) >= 0;

            if (stackInfoRet.CallerIndex != StackSourceCallStackIndex.Invalid)
            {
                StackInfo parentStackInfo = GetStackInfo(stackInfoRet.CallerIndex);

                // If our parent is also an OS function then fold into that one
                if (stackInfoRet.IsOSMethod && parentStackInfo.IsOSMethod)
                    stackInfoRet.FrameIndex = parentStackInfo.FrameIndex;

                // We fold if we have been told or there is direct recursion  
                // 
                // If we have have been told to fold, then this node disappears, so we update the 
                // slot for this stack to have exactly the information the parent had, effectivley
                // this Frame ID and the parent are synonymous (which is what folding wants to do)
                if (stackInfoRet.FrameIndex == parentStackInfo.FrameIndex)
                {
                    stackInfoRet.CloneValue(parentStackInfo);
                    return;
                }
            }
        }

        /// <summary>
        /// Represents all accumulated information about grouping for a particular stack.  Effectively this is the
        /// 'result' of applying the grouping and filtering to a particular stack.   We cache the last 100 or so
        /// of these because stacks tend to reuse the parts of the stack close the root.     
        /// </summary>
        private class StackInfo
        {
            public StackInfo()
            {
                StackIndex = StackSourceCallStackIndex.Invalid;
            }

            public StackSourceCallStackIndex StackIndex;        // This information was generated from this index. 
            public StackSourceCallStackIndex CallerIndex;       // This is just a cache of the 'GetCallerIndex call 
            public StackSourceFrameIndex FrameIndex;            // The frame index associated frame farthest from the root, it may have been morphed by grouping 

            internal bool IsOSMethod;                           // Name by which we entered the OS (null means we are not in the OS)
            internal bool InUse;                                // can't be reused.  Someone is pointing at it.  
            internal void CloneValue(StackInfo other)
            {
                // Note that I intentionally don't clone the StackIndex or IncPathsMatchedSoFarStorage as these are not the 'value' of this node. 
                FrameIndex = other.FrameIndex;
                CallerIndex = other.CallerIndex;
                IsOSMethod = other.IsOSMethod;
            }
        }

        StackSource m_baseStackSource;                          // The stack source before grouping

        /// <summary>
        /// We cache information about stacks we have previously seen so we can short-circuit work.  
        /// </summary>
        const int StackInfoCacheSize = 128;                     // Must be a power of 2;
        StackInfo[] m_stackInfoCache;
        #endregion
    }
}
