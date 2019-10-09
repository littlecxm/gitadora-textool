using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using gitadora_textool;

namespace gitadora_texbintool
{
    public class EntryInfo
    {
        public int Id;
        public int Hash;
        public int Offset;
        public string Filename;

        // For data entry
        public int Unk1;
        public int CompSize;
        public int DataOffset;
    }

    public struct RectMetadata
    {
        public int ImageId;
        public ushort X;
        public ushort Y;
        public ushort W;
        public ushort H;
        public EntryInfo Entry;
    }

    public class TexInfo
    {
        public List<RectInfo> RectInfo;

        public TexInfo()
        {
            RectInfo = new List<RectInfo>();
        }
    }

    public class FormatInfo
    {
        public string Filename;
        public byte FormatType;
    }

    public class FormatMetadata
    {
        public List<FormatInfo> FormatInfo;

        public FormatMetadata()
        {
            FormatInfo = new List<FormatInfo>();
        }
    }

    class Program
    {
        static int ReadInt32(BinaryReader reader)
        {
            var data = reader.ReadBytes(4);
            Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }

        static void WriteInt32(MemoryStream writer, int value)
        {
            var data = BitConverter.GetBytes(value);
            Array.Reverse(data);
            writer.Write(data, 0, 4);
        }

        static byte[] Decompress(BinaryReader reader)
        {
            var decompSize = ReadInt32(reader);
            var compSize = ReadInt32(reader);

            if (compSize == 0)
            {
                return reader.ReadBytes(decompSize);
            }

            var compData = reader.ReadBytes(compSize);

            byte[] windowData = new byte[4096];
            byte[] outputData = new byte[decompSize];
            int compOffset = 0, decompOffset = 0, window = 4078;

            uint controlByte = 0;
            int bitCount = 0;

            while (true)
            {
                if (bitCount == 0)
                {
                    if (compOffset >= compSize)
                    {
                        //Console.WriteLine($"compOffset >= compSize: {compOffset} >= {compSize}");
                        break;
                    }

                    controlByte = compData[compOffset++];
                    bitCount = 8;
                }

                if ((controlByte & 0x01) != 0)
                {
                    if (compOffset >= compSize)
                    {
                        //Console.WriteLine($"compOffset >= compSize: {compOffset} >= {compSize}");
                        break;
                    }

                    outputData[decompOffset] = windowData[window] = compData[compOffset];
                    decompOffset++;
                    window++;
                    compOffset++;

                    if (decompOffset >= decompSize)
                    {
                        //Console.WriteLine($"decompOffset >= decompSize: {decompOffset} >= {decompSize}");
                        break;
                    }

                    window &= 0xfff;
                }
                else
                {
                    if (decompOffset >= decompSize - 1)
                    {
                        //Console.WriteLine($"decompOffset >= decompSize - 1: {decompOffset} >= {decompSize} - 1");
                        break;
                    }

                    var slideOffset = (((compData[compOffset + 1] & 0xf0) << 4) | compData[compOffset]) & 0xfff;
                    var slideLength = (compData[compOffset + 1] & 0x0f) + 3;
                    compOffset += 2;

                    if (decompOffset + slideLength > decompSize)
                    {
                        slideLength = decompSize - decompOffset;
                    }

                    //Console.WriteLine("{0:x8} {1:x8}", slideOffset, slideLength);

                    while (slideLength > 0)
                    {
                        outputData[decompOffset] = windowData[window] = windowData[slideOffset];
                        decompOffset++;
                        window++;
                        slideOffset++;

                        window &= 0xfff;
                        slideOffset &= 0xfff;
                        slideLength--;
                    }
                }

                controlByte >>= 1;
                bitCount--;
            }

            return outputData;
        }

        public static byte[] Compress(byte[] Data)
        {
            // Based on: https://github.com/gdkchan/LegaiaText/blob/bbec0465428a9ff1858e4177588599629ca43302/LegaiaText/Legaia/Compression/LZSS.cs
            using (MemoryStream Output = new MemoryStream())
            {
                ulong[] LookUp = new ulong[0x10000];

                byte[] Dict = new byte[0x1000];

                int DictAddr = 4078;
                int SrcAddr = 0;
                int BitsAddr = 0;

                ushort Mask = 0x80;

                byte Header = 0;

                Output.Write(BitConverter.GetBytes(0), 0, 4);
                Output.Write(BitConverter.GetBytes(0), 0, 4);

                while (SrcAddr < Data.Length)
                {
                    if ((Mask <<= 1) == 0x100)
                    {
                        int OldAddr = BitsAddr;

                        BitsAddr = (int)Output.Position;

                        Output.Seek(OldAddr, SeekOrigin.Begin);
                        Output.WriteByte(Header);

                        Output.Seek(BitsAddr, SeekOrigin.Begin);
                        Output.WriteByte(0);

                        Header = 0;
                        Mask = 1;
                    }

                    int Length = 2;
                    int DictPos = 0;

                    if (SrcAddr + 2 < Data.Length)
                    {
                        int Value;

                        Value = Data[SrcAddr + 0] << 8;
                        Value |= Data[SrcAddr + 1] << 0;

                        for (int i = 0; i < 5; i++)
                        {
                            int Index = (int)((LookUp[Value] >> (i * 12)) & 0xfff);

                            //First byte doesn't match, so the others won't match too
                            if (Data[SrcAddr] != Dict[Index]) break;

                            //Temporary dictionary used on comparisons
                            byte[] CmpDict = new byte[0x1000];
                            Array.Copy(Dict, CmpDict, Dict.Length);
                            int CmpAddr = DictAddr;

                            int MatchLen = 0;

                            for (int j = 0; j < 18 && SrcAddr + j < Data.Length; j++)
                            {
                                if (CmpDict[(Index + j) & 0xfff] == Data[SrcAddr + j])
                                    MatchLen++;
                                else
                                    break;

                                CmpDict[CmpAddr] = Data[SrcAddr + j];
                                CmpAddr = (CmpAddr + 1) & 0xfff;
                            }

                            if (MatchLen > Length && MatchLen < Output.Length)
                            {
                                Length = MatchLen;
                                DictPos = Index;
                            }
                        }
                    }

                    if (Length > 2)
                    {
                        Output.WriteByte((byte)DictPos);

                        int NibLo = (Length - 3) & 0xf;
                        int NibHi = (DictPos >> 4) & 0xf0;

                        Output.WriteByte((byte)(NibLo | NibHi));
                    }
                    else
                    {
                        Header |= (byte)Mask;

                        Output.WriteByte(Data[SrcAddr]);

                        Length = 1;
                    }

                    for (int i = 0; i < Length; i++)
                    {
                        if (SrcAddr + 1 < Data.Length)
                        {
                            int Value;

                            Value = Data[SrcAddr + 0] << 8;
                            Value |= Data[SrcAddr + 1] << 0;

                            LookUp[Value] <<= 12;
                            LookUp[Value] |= (uint)DictAddr;
                        }

                        Dict[DictAddr] = Data[SrcAddr++];
                        DictAddr = (DictAddr + 1) & 0xfff;
                    }
                }

                Output.Seek(BitsAddr, SeekOrigin.Begin);
                Output.WriteByte(Header);

                Output.Seek(0, SeekOrigin.Begin);
                WriteInt32(Output, Data.Length);
                WriteInt32(Output, (int)Output.Length - 8);

                return Output.ToArray();
            }
        }

        static int CalculateHash(string input)
        {
            int hash = 0;

            foreach (var c in input)
            {
                for (int i = 0; i <= 5; i++)
                {
                    hash = (hash >> 31) & 0x4C11DB7 ^ ((hash << 1) | ((c >> i) & 1));
                }
            }

            return hash;
        }

        static void ReadDataEntrySection(BinaryReader reader, int offset, Int64 count, List<EntryInfo> entries)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);

            for (var i = 0; i < count; i++)
            {
                entries[i].Unk1 = reader.ReadInt32();
                entries[i].CompSize = reader.ReadInt32();
                entries[i].DataOffset = reader.ReadInt32();
            }
        }

        static List<EntryInfo> ReadNameSection(BinaryReader reader, int offset)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);

            var nampMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (nampMagic != "PMAN")
            {
                Console.WriteLine("Not a valid name section");
                Environment.Exit(1);
            }

            var nampSectionSize = reader.ReadInt32();
            var unk1 = reader.ReadBytes(8);
            var fileCount = reader.ReadInt32();
            var unk2 = reader.ReadBytes(8);

            var stringMetadata = new List<EntryInfo>();
            for (int i = 0; i < fileCount; i++)
            {
                int hash = reader.ReadInt32();
                int id = reader.ReadInt32();
                int strOffset = reader.ReadInt32();

                var backupOffset = reader.BaseStream.Position;

                reader.BaseStream.Seek(offset + strOffset, SeekOrigin.Begin);
                var strBytes = new List<byte>();
                while (reader.PeekChar() != 0)
                {
                    strBytes.Add(reader.ReadByte());
                }

                var str = Encoding.ASCII.GetString(strBytes.ToArray());

                stringMetadata.Add(new EntryInfo() { Offset = strOffset, Id = id, Hash = hash, Filename = str });

                reader.BaseStream.Seek(backupOffset, SeekOrigin.Begin);
            }

            stringMetadata.Sort((x, y) => x.Id.CompareTo(y.Id));

            return stringMetadata;
        }

        static List<RectMetadata> ReadRectEntrySection(BinaryReader reader, int offset)
        {
            reader.BaseStream.Seek(offset, SeekOrigin.Begin);

            var rectMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (rectMagic != "TCER")
            {
                Console.WriteLine("Not a valid rect section");
                Environment.Exit(1);
            }

            var unk1 = reader.ReadInt32();
            var unk2 = reader.ReadInt32();
            var rectSectionSize = reader.ReadInt32();
            var layerCount = reader.ReadInt32();
            var namOffset = reader.ReadInt32();
            var rectOffset = reader.ReadInt32();

            var stringMetadata = ReadNameSection(reader, offset + namOffset);

            reader.BaseStream.Seek(offset + rectOffset, SeekOrigin.Begin);

            var rectInfoMetadata = new List<RectMetadata>();
            for (int i = 0; i < layerCount; i++)
            {
                var rect = new RectMetadata();
                rect.ImageId = reader.ReadInt32();
                rect.X = reader.ReadUInt16();
                rect.W = (ushort)(reader.ReadUInt16() - rect.X);
                rect.Y = reader.ReadUInt16();
                rect.H = (ushort)(reader.ReadUInt16() - rect.Y);
                rect.Entry = stringMetadata[i];
                rectInfoMetadata.Add(rect);

                Console.WriteLine("{0:x4}x{1:x4} {2:x4}x{3:x4}", rect.X, rect.Y, rect.W, rect.H);
            }

            return rectInfoMetadata;
        }

        public static string Serialize<T>(T value, string outputFilename = null)
        {
            if (value == null)
            {
                return string.Empty;
            }
            try
            {
                var xmlserializer = new XmlSerializer(typeof(T));

                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.IndentChars = ("\t");

                XmlSerializerNamespaces namespaces = new XmlSerializerNamespaces();
                namespaces.Add(string.Empty, string.Empty);

                if (outputFilename != null)
                {
                    using (var writer = XmlWriter.Create(outputFilename, settings))
                    {
                        xmlserializer.Serialize(writer, value, namespaces);
                    }
                }

                var stringWriter = new StringWriter();

                using (var writer = XmlWriter.Create(stringWriter, settings))
                {
                    xmlserializer.Serialize(writer, value, namespaces);
                    return stringWriter.ToString();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred", ex);
            }
        }

        public static TexInfo Deserialize(string filename)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(TexInfo));

            StreamReader reader = new StreamReader(filename);
            var texInfoList = (TexInfo)serializer.Deserialize(reader);
            reader.Close();

            for (var index = 0; index < texInfoList.RectInfo.Count; index++)
            {
                texInfoList.RectInfo[index] = new RectInfo
                {
                    ExternalFilename = texInfoList.RectInfo[index].ExternalFilename,
                    Filename = texInfoList.RectInfo[index].Filename,
                    X = texInfoList.RectInfo[index].X,
                    Y = texInfoList.RectInfo[index].Y,
                    W = (ushort)(texInfoList.RectInfo[index].X + texInfoList.RectInfo[index].W),
                    H = (ushort)(texInfoList.RectInfo[index].Y + texInfoList.RectInfo[index].H),
                };
            }

            return texInfoList;
        }

        public static FormatMetadata DeserializeFormatMetadata(string filename)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(FormatMetadata));

            StreamReader reader = new StreamReader(filename);
            var formatMetdataList = (FormatMetadata)serializer.Deserialize(reader);
            reader.Close();

            for (var index = 0; index < formatMetdataList.FormatInfo.Count; index++)
            {
                formatMetdataList.FormatInfo[index] = new FormatInfo
                {
                    Filename = formatMetdataList.FormatInfo[index].Filename,
                    FormatType = formatMetdataList.FormatInfo[index].FormatType,
                };
            }

            return formatMetdataList;
        }

        static void ParseTexbinFile(string filename, bool splitImages = true)
        {
            var outputPath = Path.Combine(Path.GetDirectoryName(filename), Path.GetFileNameWithoutExtension(filename));
            Directory.CreateDirectory(outputPath);

            using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                var texpMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));

                if (texpMagic != "PXET")
                {
                    Console.WriteLine("Not a valid texbin file");
                    Environment.Exit(1);
                }

                var unk1 = reader.ReadInt32();
                var unk2 = reader.ReadInt32();
                var archiveSize = reader.ReadInt32();
                var unk3 = reader.ReadInt32();
                var fileCount = reader.ReadInt64();
                var dataOffset = reader.ReadInt32();
                var rectOffset = reader.ReadInt32();
                var unk4 = reader.ReadBytes(0x10);
                var nameOffset = reader.ReadInt32();
                var unk5 = reader.ReadInt32();
                var dataEntryOffset = reader.ReadInt32();

                if (fileCount == 0)
                {
                    Console.WriteLine("This file doesn't contain any image data.");
                    return;
                }

                var entries = ReadNameSection(reader, nameOffset);
                ReadDataEntrySection(reader, dataEntryOffset, fileCount, entries);

                var texInfo = new TexInfo();
                if (rectOffset != 0)
                {
                    var rectInfo = ReadRectEntrySection(reader, rectOffset);
                    foreach (var rect in rectInfo)
                    {
                        var e = new RectInfo
                        {
                            ExternalFilename = entries[rect.ImageId].Filename,
                            Filename = rect.Entry.Filename,
                            X = rect.X,
                            Y = rect.Y,
                            W = rect.W,
                            H = rect.H
                        };
                        texInfo.RectInfo.Add(e);
                    }

                    // Add code to optionally not split texture files and save a metadata file instead
                    if (!splitImages)
                    {
                        Serialize<TexInfo>(texInfo, Path.Combine(outputPath, "_metadata.xml"));
                    }
                }

                var formatMetadata = new FormatMetadata();
                foreach (var entry in entries)
                {
                    reader.BaseStream.Seek(entry.DataOffset, SeekOrigin.Begin);

                    var data = Decompress(reader);

                    if (Encoding.ASCII.GetString(data, 0, 4) == "TXDT"
                        || Encoding.ASCII.GetString(data, 0, 4) == "TDXT")
                    {
                        var rectInfoList = texInfo.RectInfo.Where(x =>
                            String.CompareOrdinal(Path.GetFileNameWithoutExtension(entry.Filename),
                                x.ExternalFilename) == 0).ToList();

                        if (!splitImages)
                        {
                            rectInfoList.Clear();
                        }

                        if (rectInfoList.Count == 0)
                        {
                            var rectInfo = new RectInfo
                            {
                                ExternalFilename = entry.Filename,
                                Filename = entry.Filename,
                                X = 0,
                                Y = 0,
                                W = 0,
                                H = 0
                            };
                            rectInfoList.Add(rectInfo);
                        }

                        formatMetadata.FormatInfo.Add(new FormatInfo{
                            Filename = entry.Filename,
                            FormatType = data[0x2c]
                        });

                        foreach (var rectInfo in rectInfoList)
                        {
                            try
                            {
                                using (var stream = new MemoryStream(data))
                                {
                                    using (var dataReader = new BinaryReader(stream))
                                    {
                                        byte[] extractedData;


                                        if (!splitImages || rectInfoList.Count == 0 || rectInfo.W == 0 || rectInfo.H == 0)
                                        {
                                            extractedData = gitadora_textool.Program.ExtractImageCore(dataReader, null);
                                        }
                                        else
                                        {
                                            extractedData = gitadora_textool.Program.ExtractImageCore(dataReader, rectInfo);
                                        }

                                        var ext = ".png";
                                        if (extractedData[0] == 'D' && extractedData[1] == 'D' && extractedData[2] == 'S' && extractedData[3] == ' ')
                                        {
                                            ext = ".dds";
                                        }

                                        var outputFilename = Path.Combine(outputPath, rectInfo.Filename);
                                        outputFilename += ext;

                                        Console.WriteLine("Saving {0}...", outputFilename);

                                        File.WriteAllBytes(outputFilename, extractedData);
                                        // File.WriteAllBytes(outputFilename.Replace(ext, ".bin"), data);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine("Couldn't convert image: {0}", e.Message);
                                File.WriteAllBytes(Path.Combine(outputPath, entry.Filename), data);
                            }
                        }
                    }
                }

                if (!splitImages)
                {
                    Serialize<FormatMetadata>(formatMetadata, Path.Combine(outputPath, "_metadata-format.xml"));
                }
            }
        }

        static List<byte> CreateNameSection(List<string> filelist_unique)
        {
            var nameSection = new List<byte>();
            var filenameSectionSize = 0x1c + (filelist_unique.Count * 0x0c) + filelist_unique.Select(x => x.Length + 1).Sum();
            if ((filenameSectionSize % 4) != 0)
                filenameSectionSize += 4 - (filenameSectionSize % 4);

            nameSection.AddRange(new byte[] { 0x50, 0x4D, 0x41, 0x4E });
            nameSection.AddRange(BitConverter.GetBytes(filenameSectionSize));
            nameSection.AddRange(new byte[] { 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00 });
            nameSection.AddRange(BitConverter.GetBytes(filelist_unique.Count));

            ushort a = (ushort)(1 << Convert.ToString(filelist_unique.Count >> 1, 2).Length - 1);
            ushort b = (ushort)((1 << Convert.ToString(filelist_unique.Count >> 1, 2).Length) - 1);

            nameSection.AddRange(BitConverter.GetBytes(a));
            nameSection.AddRange(BitConverter.GetBytes(b));
            nameSection.AddRange(BitConverter.GetBytes(nameSection.Count + 4));

            List<uint> hashes = filelist_unique.Select(x => (uint)CalculateHash(x)).OrderBy(x => x).ToList();
            string[] filelist_sorted = new string[filelist_unique.Count];

            foreach (var filename in filelist_unique)
            {
                filelist_sorted[hashes.IndexOf((uint)CalculateHash(filename))] = filename;
            }

            int nameBaseOffsetBase = nameSection.Count + (filelist_unique.Count * 0x0c);
            for (int i = 0; i < filelist_sorted.Length; i++)
            {
                var filelist_idx = filelist_unique.IndexOf(filelist_sorted[i]);

                nameSection.AddRange(BitConverter.GetBytes(CalculateHash(filelist_sorted[i])));
                nameSection.AddRange(BitConverter.GetBytes(filelist_idx));

                var nameBaseOffset = nameBaseOffsetBase;
                for (int j = 0; j < filelist_idx; j++)
                {
                    nameBaseOffset += filelist_unique[j].Length + 1;
                }

                nameSection.AddRange(BitConverter.GetBytes(nameBaseOffset));
            }

            for (int i = 0; i < filelist_unique.Count; i++)
            {
                nameSection.AddRange(Encoding.ASCII.GetBytes(filelist_unique[i]));
                nameSection.Add(0);
            }

            while (nameSection.Count < filenameSectionSize)
            {
                nameSection.Add(0);
            }

            return nameSection;
        }

        static void CreateTexbinFile(string pathname, bool generateRectSection = true, bool compressedData = true)
        {
            var filelist = Directory.GetFiles(pathname).Where(x => !x.ToLower().EndsWith(".xml")).ToArray();
            var filelist_unique = filelist.Select(Path.GetFileNameWithoutExtension).Distinct().Where(x => !x.ToLower().EndsWith(".xml")).ToList();
            filelist_unique = filelist_unique.Select(x => x.ToUpper()).ToList();

            if (filelist_unique.Count != filelist.Length)
            {
                Console.WriteLine("Folder has more files than expected. Are there multiple files with the same name (not including extension)?");
                Environment.Exit(1);
            }

            var formatMetadata = new FormatMetadata();
            if (File.Exists(Path.Combine(pathname, "_metadata-format.xml")))
            {
                formatMetadata = DeserializeFormatMetadata(Path.Combine(pathname, "_metadata-format.xml"));
            }
            else
            {
                foreach (var filename in filelist_unique)
                {
                    var data = new FormatInfo
                    {
                        Filename = filename,
                        FormatType = 0,
                    };
                    formatMetadata.FormatInfo.Add(data);
                }
            }

            var nameSection = CreateNameSection(filelist_unique);

            var dataSection = new List<byte>();
            var fileinfoSection = new List<byte>();
            var imageRectInfo = new Dictionary<string, Tuple<ushort, ushort>>();
            for (int i = 0; i < filelist_unique.Count; i++)
            {
                var data = File.ReadAllBytes(filelist[i]);

                Console.WriteLine("Adding {0}...", filelist[i]);

                if (!data.Take(4).SequenceEqual(new byte[] { 0x54, 0x58, 0x44, 0x54 })
                    && !data.Take(4).SequenceEqual(new byte[] { 0x54, 0x44, 0x58, 0x54 }))
                {
                    data = gitadora_textool.Program.CreateImageCore(data, true);
                }

                var formatTypeList = formatMetadata.FormatInfo.Where(x =>
                    String.CompareOrdinal(Path.GetFileNameWithoutExtension(filelist_unique[i]),
                        x.Filename) == 0).ToList();

                data[0x2c] = formatTypeList.Count > 0 ? formatTypeList[0].FormatType : data[0x2c];

                fileinfoSection.AddRange(BitConverter.GetBytes(0));
                fileinfoSection.AddRange(BitConverter.GetBytes(data.Length + 0x08));
                fileinfoSection.AddRange(BitConverter.GetBytes(0x40 + nameSection.Count + (filelist_unique.Count * 0x0c) + dataSection.Count));

                if (compressedData)
                {
                    dataSection.AddRange(Compress(data));
                }
                else
                {
                    dataSection.AddRange(BitConverter.GetBytes(data.Length).Reverse());
                    dataSection.AddRange(BitConverter.GetBytes(0));
                    dataSection.AddRange(data);
                }

                imageRectInfo[filelist_unique[i]] = new Tuple<ushort, ushort>((ushort)((data[0x11] << 8) | data[0x10]), (ushort)((data[0x13] << 8) | data[0x12]));
            }

            if ((dataSection.Count % 4) != 0)
            {
                var padding = 4 - (dataSection.Count % 4);
                while (padding > 0)
                {
                    dataSection.Add(0);
                    padding--;
                }
            }

            var rectSection = new List<byte>();
            if (generateRectSection)
            {
                var rectInfo = new TexInfo();

                if (File.Exists(Path.Combine(pathname, "_metadata.xml")))
                {
                    rectInfo = Deserialize(Path.Combine(pathname, "_metadata.xml"));
                }
                else
                {
                    foreach (var filename in filelist_unique)
                    {
                        var data = new RectInfo
                        {
                            ExternalFilename = filename,
                            Filename = filename,
                            X = 0,
                            Y = 0,
                            W = imageRectInfo[filename].Item1,
                            H = imageRectInfo[filename].Item2
                        };
                        rectInfo.RectInfo.Add(data);
                    }
                }

                var rectNameFilelist = rectInfo.RectInfo.Select(x => x.Filename)
                    .Select(Path.GetFileNameWithoutExtension).Distinct()
                    .Where(x => !x.ToLower().EndsWith(".xml")).ToList();

                var rectinfoSection = new List<byte>();
                var rectNameSection = CreateNameSection(rectNameFilelist);
                foreach (var data in rectInfo.RectInfo)
                {
                    rectinfoSection.AddRange(BitConverter.GetBytes(filelist_unique.IndexOf(Path.GetFileNameWithoutExtension(data.ExternalFilename))));
                    rectinfoSection.AddRange(BitConverter.GetBytes(data.X));
                    rectinfoSection.AddRange(BitConverter.GetBytes(data.W));
                    rectinfoSection.AddRange(BitConverter.GetBytes(data.Y));
                    rectinfoSection.AddRange(BitConverter.GetBytes(data.H));
                }

                rectSection.AddRange(
                    new byte[] { 0x54, 0x43, 0x45, 0x52, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00 });
                rectSection.AddRange(BitConverter.GetBytes(0x1c + rectNameSection.Count + rectinfoSection.Count));
                rectSection.AddRange(BitConverter.GetBytes(rectNameFilelist.Count));
                rectSection.AddRange(BitConverter.GetBytes(0x1c));
                rectSection.AddRange(BitConverter.GetBytes(0x1c + rectNameSection.Count));
                rectSection.AddRange(rectNameSection);
                rectSection.AddRange(rectinfoSection);
            }

            var outputData = new List<byte>();
            outputData.AddRange(new byte[] { 0x50, 0x58, 0x45, 0x54, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0x01, 0x00 });
            outputData.AddRange(BitConverter.GetBytes(0x40 + nameSection.Count + fileinfoSection.Count + dataSection.Count + rectSection.Count)); // Archive size
            outputData.AddRange(BitConverter.GetBytes(1));
            outputData.AddRange(BitConverter.GetBytes(filelist_unique.Count));
            outputData.AddRange(BitConverter.GetBytes(0));
            outputData.AddRange(BitConverter.GetBytes(0x40 + nameSection.Count + fileinfoSection.Count));
            outputData.AddRange(BitConverter.GetBytes(rectSection.Count > 0 ? 0x40 + nameSection.Count + fileinfoSection.Count + dataSection.Count : 0));
            outputData.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            outputData.AddRange(BitConverter.GetBytes(0x40)); // PMAN section offset
            outputData.AddRange(BitConverter.GetBytes(0));
            outputData.AddRange(BitConverter.GetBytes(0x40 + nameSection.Count));
            outputData.AddRange(nameSection);
            outputData.AddRange(fileinfoSection);
            outputData.AddRange(dataSection);
            outputData.AddRange(rectSection);

            var basePath = Path.GetFileName(pathname);
            if (String.IsNullOrWhiteSpace(basePath))
                basePath = pathname.Replace(".\\", "").Replace("\\", "");

            var outputFilename = Path.Combine(Path.GetDirectoryName(pathname), String.Format("{0}.bin", basePath));
            File.WriteAllBytes(outputFilename, outputData.ToArray());
        }

        static void Main(string[] args)
        {
            if (args.Length <= 0)
            {
                Console.WriteLine("usage: {0} [--no-rect/-nr] [--no-split/-ns] [--uncompressed/-u] input_filename", AppDomain.CurrentDomain.FriendlyName);
                Console.WriteLine("--no-rect/-nr: Don't create a rect table (Some games like Jubeat don't use the rect table)");
                Console.WriteLine("--no-split/-ns: Don't split images into separate images if they use the rect table");
                Console.WriteLine("--uncompressed/-u: Don't compress data when creating archive");
                Environment.Exit(1);
            }

            var splitImage = true;
            var generateRectSection = true;
            var compressedData = true;

            var filenames = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                if (String.CompareOrdinal(args[index].ToLower(), "--no-rect") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "--nr") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "-nr") == 0)
                {
                    generateRectSection = false;
                }
                else if (String.CompareOrdinal(args[index].ToLower(), "--no-split") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "--ns") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "-ns") == 0)
                {
                    splitImage = false;
                }
                else if (String.CompareOrdinal(args[index].ToLower(), "--uncompressed") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "--u") == 0
                    || String.CompareOrdinal(args[index].ToLower(), "-u") == 0)
                {
                    compressedData = false;
                }
                else
                {
                    filenames.Add(args[index]);
                }
            }

            foreach (var filename in filenames)
            {
                if (Directory.Exists(filename))
                {
                    CreateTexbinFile(filename, generateRectSection, compressedData);
                }
                else if (File.Exists(filename))
                {
                    ParseTexbinFile(filename, splitImage);
                }
            }
        }
    }
}
