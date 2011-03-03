//     Copyright (c) Microsoft Corporation.  All rights reserved.
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Security;
using System.Xml;
using FastSerialization;
using Diagnostics.Eventing;
using System.Text.RegularExpressions;

[SecuritySafeCritical]
public sealed class DynamicTraceEventParser : TraceEventParser
{
    public DynamicTraceEventParser(TraceEventSource source)
        : base(source)
    {
        if (source == null)         // Happens during deserialization.  
            return;

        // Try to retieve persisted state 
        state = (DynamicTraceEventParserState)StateObject;
        if (state == null)
        {
            StateObject = state = new DynamicTraceEventParserState();
            dynamicManifests = new Dictionary<Guid, DynamicManifestInfo>();

            this.source.RegisterUnhandledEvent(delegate(TraceEvent data)
            {
                if (data.Opcode != (TraceEventOpcode)0xFE)
                    return;
                if (data.ID != 0 && (byte)data.ID != 0xFE) // for classic ETW.  
                    return;

                // Look up our information. 
                DynamicManifestInfo dynamicManifest;
                if (!dynamicManifests.TryGetValue(data.ProviderGuid, out dynamicManifest))
                {
                    dynamicManifest = new DynamicManifestInfo();
                    dynamicManifests.Add(data.ProviderGuid, dynamicManifest);
                }

                ProviderManifest provider = dynamicManifest.AddChunk(data);
                // We have a completed manifest, add it to our list.  
                if (provider != null)
                    AddProvider(provider);
            });
        }
        else if (allCallbackCalled)
        {
            foreach (ProviderManifest provider in state.providers.Values)
                provider.AddProviderEvents(source, allCallback);
        }
    }

    public override event Action<TraceEvent> All
    {
        add
        {
            if (state != null)
            {
                foreach (ProviderManifest provider in state.providers.Values)
                    provider.AddProviderEvents(source, value);
            }
            if (value != null)
                allCallback += value;
            allCallbackCalled = true;
        }
        remove
        {
            throw new Exception("Not supported");
        }
    }

    #region private
    private class DynamicManifestInfo
    {
        internal DynamicManifestInfo() { }

        byte[][] Chunks;
        int ChunksLeft;
        ProviderManifest provider;
        byte majorVersion;
        byte minorVersion;
        ManifestEnvelope.ManifestFormats format;

        internal unsafe ProviderManifest AddChunk(TraceEvent data)
        {
            if (provider != null)
                return null;

            // TODO 
            if (data.EventDataLength <= sizeof(ManifestEnvelope) || data.GetByteAt(3) != 0x5B)  // magic number 
                return null;

            ushort totalChunks = (ushort)data.GetInt16At(4);
            ushort chunkNum = (ushort)data.GetInt16At(6);
            if (chunkNum >= totalChunks || totalChunks == 0)
                return null;

            if (Chunks == null)
            {
                format = (ManifestEnvelope.ManifestFormats)data.GetByteAt(0);
                majorVersion = (byte)data.GetByteAt(1);
                minorVersion = (byte)data.GetByteAt(2);
                ChunksLeft = totalChunks;
                Chunks = new byte[ChunksLeft][];
            }
            else
            {
                // Chunks have to agree with the format and version information. 
                if (format != (ManifestEnvelope.ManifestFormats)data.GetByteAt(0) ||
                    majorVersion != data.GetByteAt(1) || minorVersion != data.GetByteAt(2))
                    return null;
            }

            if (Chunks[chunkNum] != null)
                return null;

            byte[] chunk = new byte[data.EventDataLength - 8];
            Chunks[chunkNum] = data.EventData(chunk, 0, 8, chunk.Length);
            --ChunksLeft;
            if (ChunksLeft > 0)
                return null;

            // OK we have a complete set of chunks
            byte[] serializedData = Chunks[0];
            if (Chunks.Length > 1)
            {
                int totalLength = 0;
                for (int i = 0; i < Chunks.Length; i++)
                    totalLength += Chunks[i].Length;

                // Concatinate all the arrays. 
                serializedData = new byte[totalLength];
                int pos = 0;
                for (int i = 0; i < Chunks.Length; i++)
                {
                    Array.Copy(Chunks[i], 0, serializedData, pos, Chunks[i].Length);
                    pos += Chunks[i].Length;
                }
            }
            Chunks = null;
            // string str = Encoding.UTF8.GetString(serializedData);
            provider = new ProviderManifest(serializedData, format, majorVersion, minorVersion);
            return provider;
        }
    }

    private void AddProvider(ProviderManifest provider)
    {
        // Remember this serialized information.
        state.providers[provider.Guid] = provider;

        // If someone as asked for callbacks on every event, then include these too. 
        if (allCallbackCalled)
            provider.AddProviderEvents(source, allCallback);
    }

    DynamicTraceEventParserState state;
    private Dictionary<Guid, DynamicManifestInfo> dynamicManifests;
    Action<TraceEvent> allCallback;
    bool allCallbackCalled;
    #endregion
}

class DynamicTraceEventData : TraceEvent
{
    internal DynamicTraceEventData(Action<TraceEvent> action, int eventID, int task, string taskName, Guid taskGuid, int opcode, string opcodeName, Guid providerGuid, string providerName)
        : base(eventID, task, taskName, taskGuid, opcode, opcodeName, providerGuid, providerName)
    {
        Action = action;
    }

    internal protected event Action<TraceEvent> Action;
    protected internal override void Dispatch()
    {
        if (Action != null)
        {
            Action(this);
        }
    }
    public override string[] PayloadNames
    {
        get { Debug.Assert(payloadNames != null); return payloadNames; }
    }
    public override object PayloadValue(int index)
    {
        int offset = payloadFetches[index].offset;
        if (offset < 0)
            offset = SkipToField(index);
        Type type = payloadFetches[index].type;
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.String:
                return GetUnicodeStringAt(offset);
            case TypeCode.Boolean:
                return GetByteAt(offset) != 0;
            case TypeCode.Byte:
                return (byte)GetByteAt(offset);
            case TypeCode.SByte:
                return (SByte)GetByteAt(offset);
            case TypeCode.Int16:
                return GetInt16At(offset);
            case TypeCode.UInt16:
                return (UInt16)GetInt16At(offset);
            case TypeCode.Int32:
                return GetInt32At(offset);
            case TypeCode.UInt32:
                return (UInt32)GetInt32At(offset);
            case TypeCode.Int64:
                return GetInt64At(offset);
            case TypeCode.UInt64:
                return (UInt64)GetInt64At(offset);
            default:
                if (type == typeof(Guid))
                    return GetGuidAt(offset);
                throw new Exception("Unsupported type " + payloadFetches[index].type);
        }
    }
    public override string PayloadString(int index)
    {
        object value = PayloadValue(index);
        var map = payloadFetches[index].map;
        string ret = null;
        if (map != null)
        {
            long asLong = (long)((IConvertible)value).ToInt64(null);
            if (map is SortedList<long, string>)
            {
                StringBuilder sb = new StringBuilder();
                // It is a bitmap, compute the bits from the bitmap.  
                foreach (var keyValue in map)
                {
                    if (asLong == 0)
                        break;
                    if ((keyValue.Key & asLong) != 0)
                    {
                        if (sb.Length != 0)
                            sb.Append('|');
                        sb.Append(keyValue.Value);
                        asLong &= ~keyValue.Key;
                    }
                }
                if (asLong != 0)
                {
                    if (sb.Length != 0)
                        sb.Append('|');
                    sb.Append(asLong);
                }
                else if (sb.Length == 0)
                    sb.Append('0');
                ret = sb.ToString();
            }
            else
            {
                // It is a value map, just look up the value
                map.TryGetValue(asLong, out ret);
            }
        }

        if (ret == null)
            ret = value.ToString();
        return ret;
    }
    public override string FormattedMessage
    {
        get
        {
            if (MessageFormat == null)
                return null;

            // TODO is this error handling OK?  
            // Replace all %N with the string value for that parameter.  
            return Regex.Replace(MessageFormat, @"%(\d+)", delegate(Match m)
            {
                int index = int.Parse(m.Groups[1].Value) - 1;
                if ((uint)index < (uint)PayloadNames.Length)
                    return PayloadString(index);
                else
                    return "<<Out Of Range>>";
            });
        }
    }

    private int SkipToField(int index)
    {
        // Find the first field that has a fixed offset. 
        int offset = 0;
        int cur = index;
        while (0 < cur)
        {
            --cur;
            offset = payloadFetches[cur].offset;
            if (offset >= 0)
                break;
        }

        // TODO is probably does pay to remember the offsets in a particular instance, since otherwise the
        // algorithm is N*N
        while (cur < index)
        {
            short size = payloadFetches[cur].size;
            if (size < 0)
            {
                if (payloadFetches[cur].type == typeof(string))
                    offset = SkipUnicodeString(offset);
                else
                    throw new Exception("Unexpected type " + payloadFetches[cur].type.Name + " encountered.");
            }
            else
                offset += size;
            cur++;
        }
        return offset;
    }
    internal static short SizeOfType(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.String:
                return sbyte.MinValue;
            case TypeCode.Boolean:
            case TypeCode.SByte:
            case TypeCode.Byte:
                return 1;
            case TypeCode.UInt16:
            case TypeCode.Int16:
                return 2;
            case TypeCode.UInt32:
            case TypeCode.Int32:
                return 4;
            case TypeCode.UInt64:
            case TypeCode.Int64:
                return 8;
            default:
                if (type == typeof(Guid))
                    return 16;
                throw new Exception("Unsupported type " + type.Name); // TODO 
        }
    }
    internal struct PayloadFetch
    {
        public PayloadFetch(short offset, short size, Type type, IDictionary<long, string> map = null)
        {
            this.offset = offset;
            this.size = size;
            this.type = type;
            this.map = map;
        }

        public short offset;
        public short size;
        public IDictionary<long, string> map;
        public Type type;
    };

    // Fields
    internal PayloadFetch[] payloadFetches;
    internal string MessageFormat;
}

/// <summary>
/// This class is only used to pretty-print the manifest event itself.   It is pretty special purpose
/// </summary>
class DynamicManifestTraceEventData : DynamicTraceEventData
{
    internal DynamicManifestTraceEventData(Action<TraceEvent> action, ProviderManifest manifest)
        : base(action, 0xFFFE, 0, "Manifest", Guid.Empty, 0xFE, "Dump", manifest.Guid, manifest.Name)
    {
        this.manifest = manifest;
        payloadNames = new string[] { "Format", "MajorVersion", "MinorVersion", "Magic", "TotalChunks", "ChunkNumber" };
        payloadFetches = new PayloadFetch[] {
            new PayloadFetch(0, 1, typeof(byte)),
            new PayloadFetch(1, 1, typeof(byte)),
            new PayloadFetch(2, 1, typeof(byte)),
            new PayloadFetch(3, 1, typeof(byte)),
            new PayloadFetch(4, 2, typeof(ushort)),
            new PayloadFetch(6, 2, typeof(ushort)),
        };
        Action += action;
    }

    public override StringBuilder ToXml(StringBuilder sb)
    {
        int totalChunks = GetInt16At(4);
        int chunkNumber = GetInt16At(6);
        if (chunkNumber + 1 == totalChunks)
        {
            StringBuilder baseSb = new StringBuilder();
            base.ToXml(baseSb);
            sb.AppendLine(XmlUtilities.OpenXmlElement(baseSb.ToString()));
            sb.Append(manifest.Manifest);
            sb.Append("</Event>");
            return sb;
        }
        else
            return base.ToXml(sb);
    }

    #region private
    ProviderManifest manifest;
    #endregion
}

class DynamicTraceEventParserState : IFastSerializable
{
    public DynamicTraceEventParserState() { providers = new Dictionary<Guid, ProviderManifest>(); }

    internal Dictionary<Guid, ProviderManifest> providers;

    #region IFastSerializable Members

    void IFastSerializable.ToStream(Serializer serializer)
    {
        serializer.Write(providers.Count);
        foreach (ProviderManifest provider in providers.Values)
            serializer.Write(provider);
    }

    void IFastSerializable.FromStream(Deserializer deserializer)
    {
        int count;
        deserializer.Read(out count);
        for (int i = 0; i < count; i++)
        {
            ProviderManifest provider;
            deserializer.Read(out provider);
            providers.Add(provider.Guid, provider);
        }
    }

    #endregion
}

class ProviderManifest : IFastSerializable
{
    public ProviderManifest(byte[] serializedManifest, ManifestEnvelope.ManifestFormats format, byte majorVersion, byte minorVersion)
    {
        this.serializedManifest = serializedManifest;
        this.majorVersion = majorVersion;
        this.minorVersion = minorVersion;
        this.format = format;
    }

    public string Name { get { if (!inited) Init(); return name; } }
    public Guid Guid { get { if (!inited) Init(); return guid; } }
    public void AddProviderEvents(ITraceParserServices source, Action<TraceEvent> callback)
    {
        if (error != null)
            return;
        if (!inited)
            Init();
        try
        {
            Dictionary<string, int> opcodes = new Dictionary<string, int>();
            opcodes.Add("win:Info", 0);
            opcodes.Add("win:Start", 1);
            opcodes.Add("win:Stop", 2);
            opcodes.Add("win:DC_Start", 3);
            opcodes.Add("win:DC_End", 4);
            opcodes.Add("win:Extension", 5);
            opcodes.Add("win:Reply", 6);
            opcodes.Add("win:Resume", 7);
            opcodes.Add("win:Suspend", 8);
            opcodes.Add("win:Send", 9);
            opcodes.Add("win:Receive", 240);
            Dictionary<string, TaskInfo> tasks = new Dictionary<string, TaskInfo>();
            Dictionary<string, DynamicTraceEventData> templates = new Dictionary<string, DynamicTraceEventData>();
            Dictionary<string, IDictionary<long, string>> maps = null;
            Dictionary<string, string> strings = new Dictionary<string, string>();
            IDictionary<long, string> map = null;
            List<DynamicTraceEventData> events = new List<DynamicTraceEventData>();
            bool alreadyReadMyCulture = false;            // I read my culture some time in the past (I can igore things)
            string cultureBeingRead = null;
            while (reader.Read())
            {
                // TODO I currently require opcodes,and tasks BEFORE events BEFORE templates.  
                // Can be fixed by going multi-pass. 
                switch (reader.Name)
                {
                    case "event":
                        {
                            int taskNum = 0;
                            Guid taskGuid = Guid;
                            string taskName = reader.GetAttribute("task");
                            if (taskName != null)
                            {
                                TaskInfo taskInfo;
                                if (tasks.TryGetValue(taskName, out taskInfo))
                                {
                                    taskNum = taskInfo.id;
                                    taskGuid = taskInfo.guid;
                                }
                            }
                            else
                                taskName = "";

                            int opcode = 0;
                            string opcodeName = reader.GetAttribute("opcode");
                            if (opcodeName != null)
                            {
                                opcode = opcodes[opcodeName];
                                // Strip off any namespace prefix (TODO is this a good idea?
                                int colon = opcodeName.IndexOf(':');
                                if (colon >= 0)
                                    opcodeName = opcodeName.Substring(colon + 1);
                            }
                            else
                            {
                                // TODO: Improve this.  
                                // If we don't have an opcode (which is bad, since it does not work on XP), 
                                opcodeName = reader.GetAttribute("name");
                                if (taskName != null && opcodeName.StartsWith(taskName))
                                    opcodeName = opcodeName.Substring(taskName.Length);
                            }

                            int eventID = int.Parse(reader.GetAttribute("value"));
                            DynamicTraceEventData eventTemplate = new DynamicTraceEventData(
                            callback, eventID, taskNum, taskName, taskGuid, opcode, opcodeName, Guid, Name);
                            events.Add(eventTemplate);

                            string templateName = reader.GetAttribute("template");
                            if (templateName != null)
                                templates[templateName] = eventTemplate;
                            else
                            {
                                eventTemplate.payloadNames = new string[0];
                                eventTemplate.payloadFetches = new DynamicTraceEventData.PayloadFetch[0];
                            }

                            // This will be looked up in the string table in a second pass.  
                            eventTemplate.MessageFormat = reader.GetAttribute("message");
                        } break;
                    case "template":
                        {
                            string templateName = reader.GetAttribute("tid");
                            Debug.Assert(templateName != null);
                            DynamicTraceEventData eventTemplate = templates[templateName];
                            try
                            {
                                ComputeFieldInfo(eventTemplate, reader.ReadSubtree(), maps);
                            }
                            catch (Exception e)
                            {
#if DEBUG
                                Console.WriteLine("Error: Exception during processing template {0}: {1}", templateName, e.ToString());
#endif
                                throw;
                            }
                            templates.Remove(templateName);
                        } break;
                    case "opcode":
                        // TODO use message for opcode if it is available so it is localized.  
                        opcodes.Add(reader.GetAttribute("name"), int.Parse(reader.GetAttribute("value")));
                        break;
                    case "task":
                        {
                            TaskInfo info = new TaskInfo();
                            info.id = int.Parse(reader.GetAttribute("value"));
                            string guidString = reader.GetAttribute("eventGUID");
                            if (guidString != null)
                                info.guid = new Guid(guidString);
                            tasks.Add(reader.GetAttribute("name"), info);
                        } break;
                    case "valueMap":
                        map = new Dictionary<long, string>();    // value maps use dictionaries
                        goto DoMap;
                    case "bitMap":
                        map = new SortedList<long, string>();    // Bitmaps stored as sorted lists
                        goto DoMap;
                    DoMap:
                        string name = reader.GetAttribute("name");
                        var mapValues = reader.ReadSubtree();
                        while (mapValues.Read())
                        {
                            if (mapValues.Name == "map")
                            {
                                long key = int.Parse(reader.GetAttribute("value"));
                                string value = reader.GetAttribute("message");
                                map[key] = value;
                            }
                        }
                        if (maps == null)
                            maps = new Dictionary<string, IDictionary<long, string>>();
                        maps[name] = map;
                        break;
                    case "resources":
                        {
                            if (!alreadyReadMyCulture)
                            {
                                string desiredCulture = System.Globalization.CultureInfo.CurrentCulture.Name;
                                if (cultureBeingRead != null && string.Compare(cultureBeingRead, desiredCulture, StringComparison.OrdinalIgnoreCase) == 0)
                                    alreadyReadMyCulture = true;
                                cultureBeingRead = reader.GetAttribute("culture");
                            }
                        } break;
                    case "string":
                        if (!alreadyReadMyCulture)
                            strings[reader.GetAttribute("id")] = reader.GetAttribute("value");
                        break;
                }
            }

            // localize strings for maps.
            if (maps != null)
            {
                foreach (IDictionary<long, string> amap in maps.Values)
                {
                    foreach (var keyValue in new List<KeyValuePair<long, string>>(amap))
                    {
                        Match m = Regex.Match(keyValue.Value, @"^\$\(string\.(.*)\)$");
                        if (m.Success)
                        {
                            string newValue;
                            if (strings.TryGetValue(m.Groups[1].Value, out newValue))
                                amap[keyValue.Key] = newValue;
                        }
                    }
                }
            }

            // Register all the events
            foreach (var event_ in events)
            {
                // before registering, localize any message format strings.  
                string message = event_.MessageFormat;
                if (message != null)
                {
                    // Expect $(STRINGNAME) where STRINGNAME needs to be looked up in the string table
                    // TODO currently we just ignore messages without a valid string name.  Is that OK?
                    event_.MessageFormat = null;
                    Match m = Regex.Match(message, @"^\$\(string\.(.*)\)$");
                    if (m.Success)
                        strings.TryGetValue(m.Groups[1].Value, out event_.MessageFormat);
                }


                source.RegisterEventTemplate(event_);
            }

            // Create an event for the manifest event itself so it looks pretty in dumps.  
            source.RegisterEventTemplate(new DynamicManifestTraceEventData(callback, this));
        }
        catch (Exception e)
        {
            Debug.Assert(false, "Exception during manifest parsing");
            Console.WriteLine("Error: Exception during processing of in-log manifest for provider {0}.  Symbolic information may not be complete.", Name);
            error = e;
        }
        inited = false;     // If we call it again, start over from the begining.  
    }
    public string Manifest { get { if (!inited) Init(); return Encoding.UTF8.GetString(serializedManifest); } }

    #region private
    private class TaskInfo
    {
        public int id;
        public Guid guid;
    };

    private void ComputeFieldInfo(DynamicTraceEventData template, XmlReader reader, Dictionary<string, IDictionary<long, string>> maps)
    {
        List<string> payloadNames = new List<string>();
        List<DynamicTraceEventData.PayloadFetch> payloadFetches = new List<DynamicTraceEventData.PayloadFetch>();
        short offset = 0;
        while (reader.Read())
        {
            if (reader.Name == "data")
            {
                Type type = GetTypeForManifestTypeName(reader.GetAttribute("inType"));
                short size = DynamicTraceEventData.SizeOfType(type);

                // TODO There is disagreement in what win:Boolean means.  Currently it is serialized as 1 byte
                // by manage code.  However all other windows tools assume it is 4 bytes.   we are likely
                // to change this to align with windows at some point.

                payloadNames.Add(reader.GetAttribute("name"));
                IDictionary<long, string> map = null;
                string mapName = reader.GetAttribute("map");
                if (mapName != null && maps != null)
                    maps.TryGetValue(mapName, out map);
                payloadFetches.Add(new DynamicTraceEventData.PayloadFetch(offset, size, type, map));
                if (offset >= 0)
                {
                    Debug.Assert(size != 0);
                    if (size >= 0)
                        offset += size;
                    else
                        offset = short.MinValue;
                }
            }
        }
        template.payloadNames = payloadNames.ToArray();
        template.payloadFetches = payloadFetches.ToArray();
    }

    private static Type GetTypeForManifestTypeName(string manifestTypeName)
    {
        switch (manifestTypeName)
        {
            // TODO do we want to support unsigned?
            case "win:Pointer":
            case "trace:SizeT":
                return typeof(IntPtr);
            case "win:Boolean":
                return typeof(bool);
            case "win:UInt8":
            case "win:Int8":
                return typeof(byte);
            case "win:UInt16":
            case "win:Int16":
            case "trace:Port":
                return typeof(short);
            case "win:UInt32":
            case "win:Int32":
            case "trace:IPAddr":
            case "trace:IPAddrV4":
                return typeof(int);
            case "trace:WmiTime":
            case "win:UInt64":
            case "win:Int64":
                return typeof(long);
            case "win:UnicodeString":
                return typeof(string);
            case "win:GUID":
                return typeof(Guid);
            default:
                throw new Exception("Unsupported type " + manifestTypeName);
        }
    }

    #region IFastSerializable Members

    void IFastSerializable.ToStream(Serializer serializer)
    {
        serializer.Write(majorVersion);
        serializer.Write(minorVersion);
        serializer.Write((int)format);
        int count = 0;
        if (serializedManifest != null)
            count = serializedManifest.Length;
        serializer.Write(count);
        for (int i = 0; i < count; i++)
            serializer.Write(serializedManifest[i]);
    }

    void IFastSerializable.FromStream(Deserializer deserializer)
    {
        deserializer.Read(out majorVersion);
        deserializer.Read(out minorVersion);
        format = (ManifestEnvelope.ManifestFormats)deserializer.ReadInt();
        int count = deserializer.ReadInt();
        serializedManifest = new byte[count];
        for (int i = 0; i < count; i++)
            serializedManifest[i] = deserializer.ReadByte();
        Init();
    }

    private void Init()
    {
        try
        {
            XmlReaderSettings settings = new XmlReaderSettings();
            settings.IgnoreComments = true;
            settings.IgnoreWhitespace = true;
            System.IO.MemoryStream stream = new System.IO.MemoryStream(serializedManifest);
            reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.Name == "provider")
                {
                    guid = new Guid(reader.GetAttribute("guid"));
                    name = reader.GetAttribute("name");
                    fileName = reader.GetAttribute("resourceFileName");
                    break;
                }
            }

            if (name == null)
                throw new Exception("No provider element found in manifest");
        }
        catch (Exception e)
        {
            Debug.Assert(false, "Exception during manifest parsing");
            name = "";
            error = e;
        }
        inited = true;
    }

    #endregion
    private XmlReader reader;
    private byte[] serializedManifest;
    private byte majorVersion;
    private byte minorVersion;
    ManifestEnvelope.ManifestFormats format;
    private Guid guid;
    private string name;
    private string fileName;
    private bool inited;
    private Exception error;

    #endregion
}

