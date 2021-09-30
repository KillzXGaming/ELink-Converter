using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Toolbox.Core.IO;
using Newtonsoft.Json;

namespace ELinkConverter
{
    public class ELink
    {
        /// <summary>
        /// A list of effect assets used in the game.
        /// </summary>
        public Dictionary<string, UserHeader> Effects = new Dictionary<string, UserHeader>();

        public uint Version { get; set; }

        public ELink() { }

        public ELink(string fileName)
        {
            using (var reader = new FileReader(fileName)) {
                Read(reader);
            }
        }

        public ELink(Stream stream)
        {
            using (var reader = new FileReader(stream))
            {
                Read(reader);
            }
        }

        void Read(FileReader reader)
        {
            //Make sure the encoder supports SHIFT JIS (required on net standard libs)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            //Big endian BOM
            reader.ByteOrder = Syroot.BinaryData.ByteOrder.BigEndian;
            //Read file header
            reader.ReadSignature(4, "eflk");
            Version = reader.ReadUInt32();
            uint EmitterCount = reader.ReadUInt32();
            uint StringTableOffset = reader.ReadUInt32();

            //Read each effect list
            for (int i = 0; i < EmitterCount; i++)
            {
                uint dataOffset = reader.ReadUInt32();
                uint nameOffset = reader.ReadUInt32();

                UserHeader effectList = new UserHeader();
                using (reader.TemporarySeek(StringTableOffset + nameOffset, SeekOrigin.Begin)) {
                    effectList.Name = reader.ReadZeroTerminatedString(Encoding.GetEncoding("Shift-JIS"));
                }
                using (reader.TemporarySeek(dataOffset, SeekOrigin.Begin)) {
                    effectList.Read(reader);
                }
                Effects.Add(effectList.Name, effectList);
            }
        }

        /// <summary>
        /// Imports a .json file as ELink
        /// </summary>
        public static ELink Import(string fileName)
        {
            return JsonConvert.DeserializeObject<ELink>(File.ReadAllText(fileName));
        }

        /// <summary>
        /// Exports elink as .json
        /// </summary>
        public void Export(string fileName)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(fileName, json);
        }

        /// <summary>
        /// Saves the elink binary file.
        /// </summary>
        public void Save(string fileName)
        {
            using (var stream = new FileStream(fileName, FileMode.Create, FileAccess.Write)) {
                Save(stream);
            }
        }

        /// <summary>
        /// Saves the elink binary file.
        /// </summary>
        public void Save(Stream stream)
        {
            using (var writer = new FileWriter(stream)) {
                Write(writer);
            }
        }

        void Write(FileWriter writer)
        {
            //Big endian bom
            writer.ByteOrder = Syroot.BinaryData.ByteOrder.BigEndian;
            writer.WriteSignature("eflk");
            writer.Write(Version);
            writer.Write(Effects.Count);
            //Allocate string table offset spot
            writer.Write(uint.MaxValue);
            //Allocate offset spots
            for (int i = 0; i < Effects.Count; i++)
            {
                writer.Write(uint.MaxValue);
                writer.Write(uint.MaxValue);
            }

            //Save each effect with offset set
            for (int i = 0; i < Effects.Count; i++)
            {
                writer.WriteUint32Offset(16 + (i * 8));
                Effects.Values.ElementAt(i).Write(writer);
            }
            //Set string table ioffset
            long stringTablePos = writer.Position;
            writer.WriteUint32Offset(12);

            for (int i = 0; i < Effects.Count; i++)
            {
                //Save each string with Shift JIS encoding
                string name = Effects.Values.ElementAt(i).Name;

                writer.WriteUint32Offset(20 + (i * 8), stringTablePos);
                writer.WriteString(name, Encoding.GetEncoding("Shift-JIS"));
            }
        }
    }

    /// <summary>
    /// Represents a user data header storing effect resources.
    /// </summary>
    public class UserHeader
    {
        /// <summary>
        /// The user header name of the asset.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Effect resources storing asset params.
        /// </summary>
        public Dictionary<string, EffectResource> EffectResources = new Dictionary<string, EffectResource>();

        /// <summary>
        /// Effect actions storing asset triggers.
        /// </summary>
        public Dictionary<string, ResAction> EmitterActions = new Dictionary<string, ResAction>();

        public UserHeader() { }

        public void Read(FileReader reader)
        {
            long pos = reader.Position;

            uint unk = reader.ReadUInt32(); //9
            uint numEmitterParam = reader.ReadUInt32(); 
            uint numInstances = reader.ReadUInt32(); 
            uint numEmitterSet = reader.ReadUInt32(); 
            uint numAction = reader.ReadUInt32(); 
            uint stringTablePos = (uint)pos + reader.ReadUInt32();
            uint unk2 = reader.ReadUInt32(); //4

            List<AssetParam> emitterSets = new List<AssetParam>();
            List<ResTrigger> instances = new List<ResTrigger>();

            //Load asset params
            for (int i = 0; i < numEmitterParam; i++)
            {
                var emitterSet = new AssetParam(reader, stringTablePos);
                emitterSets.Add(emitterSet);
            }

            //Load callback table to the emitter param table.
            for (int i = 0; i < numEmitterParam; i++)
                emitterSets[i].CallbackIndices = new ResCallTable(reader);

            //Load effect resources and add the asset params
            for (int i = 0; i < numEmitterSet; i++)
            {
                var emitterRes = new EffectResource(reader, emitterSets, stringTablePos);
                EffectResources.Add($"Resource{i}", emitterRes);
            }

            //Load res triggers
            for (int i = 0; i < numInstances; i++)
            {
                var effectInstance = new ResTrigger(reader, stringTablePos);
                instances.Add(effectInstance);
            }

            //Load actions and add the res triggers
            for (int i = 0; i < numAction; i++)
            {
                var effectAction = new ResAction(reader, instances, stringTablePos);
                EmitterActions.Add($"Action{i}", effectAction);
            }
        }

        public void Write(FileWriter writer)
        {
            long pos = writer.Position;
            Dictionary<long, string> stringTable = new Dictionary<long, string>();

            //Get asset params from the resources
            var assetParams = this.EffectResources.SelectMany(x => x.Value.AssetParams.Values).ToList();
            //Get asset triggers from the actions
            var triggers = this.EmitterActions.SelectMany(x => x.Value.Triggers.Values).ToList();

            writer.Write(9);
            writer.Write(assetParams.Count);
            writer.Write(triggers.Count);
            writer.Write(EffectResources.Count);
            writer.Write(EmitterActions.Count);
            //Allocate string table offset
            writer.Write(uint.MaxValue);
            writer.Write(4);

            foreach (var prop in assetParams)
            {
                //Update the index
                prop.Index = assetParams.IndexOf(prop);
                //Write the asset params
                prop.Write(writer, stringTable);
            }
            //Write the callback table
            foreach (var prop in assetParams)
                prop.CallbackIndices.Write(writer);
            //Write the effect resources
            foreach (var prop in EffectResources.Values)
                prop.Write(writer, assetParams, stringTable);
            //Write the res triggers
            foreach (var prop in triggers)
            {
                prop.Index = triggers.IndexOf(prop);
                prop.Write(writer, stringTable);
            }
            //Write the res actions
            foreach (var prop in EmitterActions.Values)
                prop.Write(writer, triggers, stringTable);

            //Write string table offset
            long stringTablePos = writer.Position;
            writer.WriteUint32Offset(pos + 20, pos);

            Dictionary<string, long> writtenStrings = new Dictionary<string, long>();
            foreach (var str in stringTable) {
                //Point to existing written strings in the table if necessary
                if (writtenStrings.ContainsKey(str.Value))
                {
                    //Write the offset 
                    using (writer.TemporarySeek(str.Key, SeekOrigin.Begin)) {
                        writer.Write((uint)(writtenStrings[str.Value] - stringTablePos));
                    }
                }
                else
                {
                    long strPos = writer.Position;
                    //Write the offset and string
                    writer.WriteUint32Offset(str.Key, stringTablePos);
                    writer.WriteString(str.Value);

                    writtenStrings.Add(str.Value, strPos);
                }
            }
            //Align the asset section by 16 bytes with 0xFF filled in.
            writer.AlignBytes(16, 0xFF);
        }

        /// <summary>
        /// Imports the user data header from a json file.
        /// </summary>
        public static UserHeader Import(string fileName)
        {
            return JsonConvert.DeserializeObject<UserHeader>(File.ReadAllText(fileName));
        }

        /// <summary>
        /// Exports the user data header to a json file.
        /// </summary>
        public void Export(string fileName)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(fileName, json);
        }

        /// <summary>
        /// The asset param of an emitter set.
        /// </summary>
        public class AssetParam
        {
            /// <summary>
            ///  Gets or sets the name of the emitter set asset.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Gets or sets the emitter set scale.
            /// </summary>
            public float Scale { get; set; }

            /// <summary>
            /// Gets or sets the emitter set position.
            /// </summary>
            public float[] Position { get; set; }

            /// <summary>
            /// Gets or sets the emitter set rotation in radians.
            /// </summary>
            public float[] Rotate { get; set; }

            /// <summary>
            /// Gets or sets the emitter set color.
            /// </summary>
            public float[] RGBA { get; set; }

            /// <summary>
            /// Gets or sets collision attribute.
            /// </summary>
            public string Attribute { get; set; }

            /// <summary>
            /// Gets or sets collision water state.
            /// </summary>
            public string State { get; set; }

            /// <summary>
            /// Gets or sets group map name.
            /// </summary>
            public string MapName { get; set; }

            /// <summary>
            /// Gets or sets bone name to bind to.
            /// </summary>
            public string BoneName { get; set; }

            /// <summary>
            /// Gets or sets a param used by area cameras.
            /// </summary>
            public string CamParam { get; set; }

            public int Index;

            public uint[] Unknowns4 { get; set; }
            public float Unknowns5 { get; set; }

            /// <summary>
            /// Gets or sets the callback table for callback indices.
            /// </summary>
            public ResCallTable CallbackIndices { get; set; }

            public AssetParam() { }

            public AssetParam(FileReader reader, uint stringTableOffset)
            {
                Index = reader.ReadInt32();
                reader.ReadUInt32();
                Attribute = ReadName(reader, stringTableOffset);
                reader.ReadUInt32();
                State = ReadName(reader, stringTableOffset);
                reader.ReadUInt32();
                MapName = ReadName(reader, stringTableOffset);
                reader.ReadUInt32();
                Name = ReadName(reader, stringTableOffset);
                reader.ReadUInt32();

                BoneName = ReadName(reader, stringTableOffset);
                CamParam = ReadName(reader, stringTableOffset);
                Unknowns4 = reader.ReadUInt32s(2);
                Unknowns5 = reader.ReadSingle();
                Scale = reader.ReadSingle();
                Position = reader.ReadSingles(3);
                Rotate = reader.ReadSingles(3);
                RGBA = reader.ReadSingles(4);
            }

            public void Write(FileWriter writer, Dictionary<long, string> stringTable)
            {
                writer.Write(Index);
                writer.Write(0);
                stringTable.Add(writer.Position, Attribute);
                writer.Write(0);
                writer.Write(0);
                stringTable.Add(writer.Position, State);
                writer.Write(0);
                writer.Write(0);
                stringTable.Add(writer.Position, MapName);
                writer.Write(0);
                writer.Write(0);
                stringTable.Add(writer.Position, Name);
                writer.Write(0);
                writer.Write(0);
                stringTable.Add(writer.Position, BoneName);
                writer.Write(0);
                stringTable.Add(writer.Position, CamParam);
                writer.Write(0);
                writer.Write(Unknowns4);
                writer.Write(Unknowns5);
                writer.Write(Scale);
                writer.Write(Position);
                writer.Write(Rotate);
                writer.Write(RGBA);
            }
        }

        /// <summary>
        /// Gets or sets the callback table for callback indices.
        /// </summary>
        public class ResCallTable
        {
            public int Index1 { get; set; }
            public int Index2 { get; set; }
            public int Index3 { get; set; }
            public int Index4 { get; set; }

            public ResCallTable() { }

            public ResCallTable(FileReader reader)
            {
                Index1 = reader.ReadInt32();
                Index2 = reader.ReadInt32();
                Index3 = reader.ReadInt32();
                Index4 = reader.ReadInt32();
            }

            public void Write(FileWriter writer)
            {
                writer.Write(Index1);
                writer.Write(Index2);
                writer.Write(Index3);
                writer.Write(Index4);
            }
        }

        /// <summary>
        /// Gets or sets the effect resource storing asset effect parameters..
        /// </summary>
        public class EffectResource
        {
            /// <summary>
            /// Gets or sets the effect resource name.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Gets or sets the asset param lookup.
            /// </summary>
            [JsonProperty(ItemConverterType = typeof(NoFormattingConverter))]
            public Dictionary<string, AssetParam> AssetParams { get; set; } = new Dictionary<string, AssetParam>();

            public EffectResource() { }

            public EffectResource(FileReader reader, List<AssetParam> emitterSets, uint stringTableOffset)
            {
                Name = ReadName(reader, stringTableOffset);
                reader.ReadUInt32s(3); //always 0
                                       //Indices for which emitter sets to use (start and end index)
                ushort startIndex = reader.ReadUInt16();
                ushort endIndex = reader.ReadUInt16();

                AssetParams.Clear();
                for (int i = startIndex; i <= endIndex; i++)
                    AssetParams.Add($"EmitterSet{i}", emitterSets[i]);
            }

            public void Write(FileWriter writer, List<AssetParam> emitterSets, Dictionary<long, string> stringTable)
            {
                stringTable.Add(writer.Position, Name);
                writer.Write(uint.MaxValue);
                writer.Write(new uint[3]);
                writer.Write((ushort)emitterSets.IndexOf(AssetParams.FirstOrDefault().Value));
                writer.Write((ushort)emitterSets.IndexOf(AssetParams.LastOrDefault().Value));
            }
        }

        /// <summary>
        /// Stores effect trigger data performed by an action.
        /// </summary>
        public class ResTrigger
        {
            public int Index;

            /// <summary>
            /// The resource name to execute on.
            /// </summary>
            public string ResourceName { get; set; }

            /// <summary>
            /// The bone name to bind to.
            /// </summary>
            public string BoneName { get; set; }

            /// <summary>
            /// The delay time in frames to execute on.
            /// </summary>
            public uint Delay { get; set; }

            /// <summary>
            /// The emission rate/speed.
            /// </summary>
            public float EmissionRate { get; set; }

            /// <summary>
            /// The offset of the emitter resource placed in local coordinates.
            /// </summary>
            public float[] Offset { get; set; }

            public int Unknown3 { get; set; }

            public ushort[] Unknown4 { get; set; }

            public uint Unknown { get; set; }

            public float[] Unknowns5 { get; set; }

            public ResTrigger() { }

            public ResTrigger(FileReader reader, uint stringTableOffset)
            {
                Index = reader.ReadInt32();
                Unknown = reader.ReadUInt32();
                ResourceName = ReadName(reader, stringTableOffset);
                reader.ReadUInt32();
                Delay = reader.ReadUInt32();
                Unknown3 = reader.ReadInt32();
                Unknown4 = reader.ReadUInt16s(2);
                BoneName = ReadName(reader, stringTableOffset);
                EmissionRate = reader.ReadSingle();
                Offset = reader.ReadSingles(3);
                Unknowns5 = reader.ReadSingles(3);
            }

            public void Write(FileWriter writer, Dictionary<long, string> stringTable)
            {
                writer.Write(Index);
                writer.Write(Unknown);
                stringTable.Add(writer.Position, ResourceName);
                writer.Write(uint.MaxValue);
                writer.Write(0);
                writer.Write(Delay);
                writer.Write(Unknown3);
                writer.Write(Unknown4);
                stringTable.Add(writer.Position, BoneName);
                writer.Write(uint.MaxValue);
                writer.Write(EmissionRate);
                writer.Write(Offset);
                writer.Write(Unknowns5);
            }
        }

        /// <summary>
        /// Gets or sets the res action of an effect to run triggers.
        /// </summary>
        public class ResAction
        {
            /// <summary>
            /// Gets or sets the trigger name.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Gets or sets the trigger name.
            /// </summary>
            [JsonProperty(ItemConverterType = typeof(NoFormattingConverter))]
            public Dictionary<string, ResTrigger> Triggers { get; set; } = new Dictionary<string, ResTrigger>();

            public ResAction() { }

            public ResAction(FileReader reader, List<ResTrigger> instances, uint stringTableOffset)
            {
                Name = ReadName(reader, stringTableOffset);
                reader.ReadUInt32(); //always 0
                ushort startIndex = reader.ReadUInt16();
                ushort endIndex = reader.ReadUInt16();

                Triggers.Clear();
                for (int i = startIndex; i <= endIndex; i++)
                    Triggers.Add($"action{i}", instances[i]);
            }

            public void Write(FileWriter writer, List<ResTrigger> instances, Dictionary<long, string> stringTable)
            {
                stringTable.Add(writer.Position, Name);
                writer.Write(uint.MaxValue);
                writer.Write(0);
                writer.Write((ushort)instances.IndexOf(Triggers.FirstOrDefault().Value));
                writer.Write((ushort)instances.IndexOf(Triggers.LastOrDefault().Value));
            }
        }

        static string ReadName(FileReader reader, uint stringTableOffset)
        {
            uint offset = reader.ReadUInt32();
            using (reader.TemporarySeek(stringTableOffset + offset, SeekOrigin.Begin)) {
                return reader.ReadZeroTerminatedString();
            }
        }
    }
}
