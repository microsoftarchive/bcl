// Copyright (c) Microsoft Corporation.  All rights reserved
using System;
using System.Collections.Generic;
using System.Text;
using Diagnostics.Eventing;
using System.Diagnostics;

class EventStats
{
    public EventStats() { }
    public EventStats(TraceEventDispatcher source)
        : this()
    {
        Action<TraceEvent> StatsCollector = delegate(TraceEvent data)
        {
            this.Increment(data);
        };

        // Add my parsers.
        source.Clr.All += StatsCollector;
        source.Kernel.All += StatsCollector;
        new ClrRundownTraceEventParser(source).All += StatsCollector;

        source.UnhandledEvent += StatsCollector;
        source.Process();
    }

    public TaskStats this[string taskName]
    {
        get
        {
            TaskStats ret;
            if (!Tasks.TryGetValue(taskName, out ret))
            {
                ret = new TaskStats();
                ret.Name = taskName;
                Tasks.Add(taskName, ret);
            }
            return ret;
        }
    }
    public int Count;
    public int StackCount;
    public Dictionary<string, TaskStats> Tasks = new Dictionary<string, TaskStats>();
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        ToString(sb);
        return sb.ToString();
    }
    public void ToString(StringBuilder sb)
    {
        sb.Append("<Stats");
        sb.Append(" Count=\"").Append(Count).Append("\"");
        sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
        sb.Append(">").AppendLine();

        List<TaskStats> tasks = new List<TaskStats>(Tasks.Values);
        tasks.Sort((x, y) => y.Count - x.Count);

        foreach (TaskStats task in tasks)
            task.ToString(sb);

        sb.AppendLine("</Stats>");
    }

    internal void Increment(TraceEvent data)
    {
#if DEBUG
                // Debug.Assert((byte)data.opcode != unchecked((byte)-1));        // Means PrepForCallback not done. 
                Debug.Assert(data.TaskName != "ERRORTASK");
                Debug.Assert(data.OpcodeName != "ERROROPCODE");
#endif
        Count++;
        TaskStats task = this[data.TaskName];
        if (task.ProviderName == null)
            task.ProviderName = data.ProviderName;

        CallStackIndex index = data.CallStackIndex();
        bool hasStack = (index != CallStackIndex.Invalid);
        if (hasStack)
            StackCount++;
        task.Increment(data.OpcodeName, hasStack);
        StackWalkTraceData asStackWalk = data as StackWalkTraceData;
        if (asStackWalk != null)
        {
            StackWalkStats stackWalkStats = task.ExtraData as StackWalkStats;
            if (stackWalkStats == null)
            {
                stackWalkStats = new StackWalkStats();
                task.ExtraData = stackWalkStats;
            }
            stackWalkStats.Log(asStackWalk);
        }
    }
}

class StackWalkStats
{
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("<Stacks Count=\"").Append(stacks).Append("\"");
        sb.Append(" Frames=\"").Append(frames).Append("\"");
        if (stacks > 0)
        {
            double average = (double)frames / stacks;
            sb.Append(" AverageCount=\"").Append(average.ToString("f1")).Append("\"");
        }
        sb.Append(" DistictIP=\"").Append(counts.Count).Append("\"");
        sb.Append("/>").AppendLine();
        return sb.ToString();
    }
    internal unsafe void Log(StackWalkTraceData data)
    {
        stacks++;
        for (int i = 0; i < data.FrameCount; i++)
        {
            int value = 0;
            Address ip = data.InstructionPointer(i);
            counts.TryGetValue((long)ip, out value);
            value++;
            counts[(long)ip] = value;
            frames++;
        }
    }

    int stacks;
    int frames;
    Dictionary<long, int> counts = new Dictionary<long, int>();
}

class TaskStats
{
    public void Increment(string opcodeName, bool hasStack)
    {
        if (hasStack)
            StackCount++;
        Count++;

        OpcodeStats opcodeStats;
        if (!Opcodes.TryGetValue(opcodeName, out opcodeStats))
        {
            opcodeStats = new OpcodeStats(opcodeName);
            Opcodes.Add(opcodeName, opcodeStats);
        }
        opcodeStats.Increment(hasStack);
    }
    public void ToString(StringBuilder sb)
    {
        sb.Append("  <Task Name=\"").Append(Name).Append("\"");
        sb.Append(" Count=\"").Append(Count).Append("\"");
        sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
        if (StackCount > 0)
        {
            double percent = 100.0 * StackCount / Count;
            sb.Append(" PercentWithStacks=\"").Append(percent.ToString("f1")).Append("\"");
        }
        sb.Append(" ProviderName=\"").Append(ProviderName).Append("\"");
        sb.Append(">").AppendLine();

        List<string> opcodeNames = new List<string>(Opcodes.Keys);
        opcodeNames.Sort((x, y) => Opcodes[y].Count - Opcodes[x].Count);

        foreach (string opcodeName in opcodeNames)
            sb.Append(Opcodes[opcodeName].ToString());

        if (ExtraData != null)
            sb.Append(ExtraData.ToString()).AppendLine();
        sb.AppendLine("  </Task>");
    }
    public string ProviderName;
    public string Name;
    public int Count;
    public int StackCount;
    public object ExtraData;
    public Dictionary<string, OpcodeStats> Opcodes = new Dictionary<string, OpcodeStats>();
}

class OpcodeStats
{
    public OpcodeStats(string name)
    {
        Name = name;
    }
    public void Increment(bool hasStack)
    {
        Count++;
        if (hasStack)
            StackCount++;
    }
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("     <Opcode Name=\"").Append(Name).Append("\"");
        sb.Append(" Count=\"").Append(Count).Append("\"");
        sb.Append(" StackCount=\"").Append(StackCount).Append("\"");
        if (StackCount > 0)
        {
            double percent = 100.0 * StackCount / Count;
            sb.Append(" PercentWithStacks=\"").Append(percent.ToString("f1")).Append("\"");
        }
        sb.Append("/>").AppendLine();
        return sb.ToString();
    }
    public int Count;
    public int StackCount;
    public string Name;
}
