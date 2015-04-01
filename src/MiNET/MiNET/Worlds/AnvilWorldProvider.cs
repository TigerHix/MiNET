﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using fNbt;
using log4net;
using MiNET.Utils;

namespace MiNET.Worlds
{
	public class AnvilWorldProvider : IWorldProvider
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (AnvilWorldProvider));

		private List<int> _gaps;
		private List<int> _ignore;
		private byte _waterOffsetY;
		private FlatlandWorldProvider _flatland;
		private LevelInfo _level;
		private readonly ConcurrentDictionary<ChunkCoordinates, ChunkColumn> _chunkCache = new ConcurrentDictionary<ChunkCoordinates, ChunkColumn>();
		private string _basePath;

		public bool IsCaching { get; private set; }


		public AnvilWorldProvider()
		{
			IsCaching = true;
			_flatland = new FlatlandWorldProvider();
		}

		public void Initialize()
		{
			_basePath = ConfigParser.GetProperty("PCWorldFolder", "World").Trim();

			NbtFile file = new NbtFile();
			file.LoadFromFile(Path.Combine(_basePath, "level.dat"));
			NbtTag dataTag = file.RootTag["Data"];
			_level = new LevelInfo(dataTag);

			_waterOffsetY = (byte) ConfigParser.GetProperty("PCWaterOffset", 0);

			_ignore = new List<int>();
			_ignore.Add(23);
			_ignore.Add(25);
			_ignore.Add(28);
			_ignore.Add(29);
			_ignore.Add(33);
			_ignore.Add(34);
			_ignore.Add(36);
			_ignore.Add(55);
			_ignore.Add(69);
			_ignore.Add(70);
			_ignore.Add(71);
			_ignore.Add(72);
//			_ignore.Add(75);
//			_ignore.Add(76);
			_ignore.Add(77);
			_ignore.Add(84);
			_ignore.Add(87);
			_ignore.Add(88);
			_ignore.Add(93);
			_ignore.Add(94);
			_ignore.Add(97);
			_ignore.Add(113);
			_ignore.Add(115);
			_ignore.Add(117);
			_ignore.Add(118);
//			_ignore.Add(123);
			_ignore.Add(131);
			_ignore.Add(132);
			_ignore.Add(138);
			_ignore.Add(140);
			_ignore.Add(143);
			_ignore.Add(144);
			_ignore.Add(145);
			_ignore.Sort();

			_gaps = new List<int>();
			_gaps.Add(23);
			_gaps.Add(25);
//			_gaps.Add(27);
			_gaps.Add(28);
			_gaps.Add(29);
			_gaps.Add(33);
			_gaps.Add(34);
			_gaps.Add(36);
			_gaps.Add(55);
//			_gaps.Add(66);
			_gaps.Add(69);
			_gaps.Add(70);
			_gaps.Add(72);
			_gaps.Add(75);
			_gaps.Add(76);
			_gaps.Add(77);
			_gaps.Add(84);
//			_gaps.Add(87);
			_gaps.Add(88);
			_gaps.Add(90);
			_gaps.Add(93);
			_gaps.Add(94);
			_gaps.Add(95);
			_gaps.Add(97);
//			_gaps.Add(99);
//			_gaps.Add(100);
//			_gaps.Add(106);
//			_gaps.Add(111);
			_gaps.Add(115);
			_gaps.Add(116);
			_gaps.Add(117);
			_gaps.Add(118);
			_gaps.Add(119);
//			_gaps.Add(120);
//			_gaps.Add(121);
			_gaps.Add(122);
			_gaps.Add(123);
			_gaps.Add(124);
			_gaps.Add(125);
			_gaps.Add(126);
//			_gaps.Add(127);
			_gaps.Add(130);
			_gaps.Add(131);
			_gaps.Add(132);
			_gaps.Add(137);
			_gaps.Add(138);
			_gaps.Add(140);
			_gaps.Add(143);
			_gaps.Add(144);
			_gaps.Add(145);
			_gaps.Add(146);
			_gaps.Add(147);
			_gaps.Add(148);
			_gaps.Add(149);
			_gaps.Add(150);
			_gaps.Add(151);
			_gaps.Add(152);
			_gaps.Add(153);
			_gaps.Add(154);
			_gaps.Add(160);
			_gaps.Add(165);
			_gaps.Add(166);
			_gaps.Add(167);
			_gaps.Add(168);
			_gaps.Add(169);
			_gaps.Sort();
		}

		public ChunkColumn GenerateChunkColumn(ChunkCoordinates chunkCoordinates)
		{
			ChunkColumn cachedChunk;
			if (_chunkCache.TryGetValue(chunkCoordinates, out cachedChunk)) return cachedChunk;

			ChunkColumn chunk = GetChunk(chunkCoordinates);

			_chunkCache[chunkCoordinates] = chunk;

			return chunk;
		}

		public ChunkColumn GetChunk(ChunkCoordinates coordinates)
		{
			int width = 32;
			int depth = 32;

			int rx = coordinates.X >> 5;
			int rz = coordinates.Z >> 5;

			string filePath = Path.Combine(_basePath, string.Format(@"region\r.{0}.{1}.mca", rx, rz));

			if (!File.Exists(filePath)) return _flatland.GenerateChunkColumn(coordinates);

			var regionFile = File.OpenRead(filePath);

			byte[] buffer = new byte[8192];
			regionFile.Read(buffer, 0, 8192);

			int tableOffset = ((coordinates.X%width) + (coordinates.Z%depth)*width)*4;

			regionFile.Seek(tableOffset, SeekOrigin.Begin);
			byte[] offsetBuffer = new byte[4];
			regionFile.Read(offsetBuffer, 0, 3);
			Array.Reverse(offsetBuffer);
			int offset = BitConverter.ToInt32(offsetBuffer, 0) << 4;

			int length = regionFile.ReadByte();

			if (offset == 0 || length == 0) return _flatland.GenerateChunkColumn(coordinates);

			regionFile.Seek(offset, SeekOrigin.Begin);
			byte[] waste = new byte[4];
			regionFile.Read(waste, 0, 4);
			int compressionMode = regionFile.ReadByte();

			var nbt = new NbtFile();
			nbt.LoadFromStream(regionFile, NbtCompression.ZLib);

			NbtTag dataTag = nbt.RootTag["Level"];

			NbtList sections = dataTag["Sections"] as NbtList;

			ChunkColumn chunk = new ChunkColumn
			{
				x = coordinates.X,
				z = coordinates.Z,
				biomeId = dataTag["Biomes"].ByteArrayValue
			};

			for (int i = 0; i < chunk.biomeId.Length; i++)
			{
				if (chunk.biomeId[i] > 22) chunk.biomeId[i] = 0;
			}
			if (chunk.biomeId.Length > 256) throw new Exception();

			// This will turn into a full chunk column
			foreach (NbtTag sectionTag in sections)
			{
				int sy = sectionTag["Y"].ByteValue*16;
				byte[] blocks = sectionTag["Blocks"].ByteArrayValue;
				byte[] data = sectionTag["Data"].ByteArrayValue;
				NbtTag addTag = sectionTag["Add"];
				byte[] adddata = new byte[2048];
				if (addTag != null) adddata = addTag.ByteArrayValue;
				byte[] blockLight = sectionTag["BlockLight"].ByteArrayValue;
				byte[] skyLight = sectionTag["SkyLight"].ByteArrayValue;

				for (int x = 0; x < 16; x++)
				{
					for (int z = 0; z < 16; z++)
					{
						for (int y = 0; y < 16; y++)
						{
							int yi = sy + y - _waterOffsetY;
							if (yi < 0 || yi >= 128) continue;

							int anvilIndex = y*16*16 + z*16 + x;
							int blockId = blocks[anvilIndex] + (Nibble4(adddata, anvilIndex) << 8);

							// Anvil to PE friendly converstion
							if (blockId == 125) blockId = 5;
							else if (blockId == 126) blockId = 158;
							else if (blockId == 75) blockId = 50;
							else if (blockId == 76) blockId = 50;
							else if (blockId == 123) blockId = 89;
							else if (blockId == 124) blockId = 89;
							else if (blockId == 152) blockId = 73;
							else if (_ignore.BinarySearch(blockId) >= 0) blockId = 0;
							else if (_gaps.BinarySearch(blockId) >= 0)
							{
								Debug.WriteLine("Missing material: " + blockId);
								blockId = 133;
							}

							if (blockId > 255) blockId = 41;

							if (yi == 127 && blockId != 0) blockId = 30;
							if (yi == 0 && (blockId == 8 || blockId == 9 || blockId == 0)) blockId = 7;

							//if (blockId != 0) blockId = 41;

							chunk.SetBlock(x, yi, z, (byte) blockId);
							chunk.SetMetadata(x, yi, z, Nibble4(data, anvilIndex));
							chunk.SetBlocklight(x, yi, z, Nibble4(blockLight, anvilIndex));
							chunk.SetSkylight(x, yi, z, Nibble4(skyLight, anvilIndex));
						}
					}
				}
			}

			NbtList entities = dataTag["Entities"] as NbtList;
			NbtList blockEntities = dataTag["TileEntities"] as NbtList;
			NbtList tileTicks = dataTag["TileTicks"] as NbtList;

			return chunk;
		}

		private byte Nibble4(byte[] arr, int index)
		{
			return (byte) (index%2 == 0 ? arr[index/2] & 0x0F : (arr[index/2] >> 4) & 0x0F);
		}

		public Vector3 GetSpawnPoint()
		{
			var spawnPoint = new Vector3(_level.SpawnX, _level.SpawnY, _level.SpawnZ);
			spawnPoint.Y += 2; // Compensate for point being at head
			spawnPoint.Y += _waterOffsetY; // Compensate for offset
			if (spawnPoint.Y > 127) spawnPoint.Y = 127;
			return spawnPoint;
		}

		public void SaveChunks()
		{
		}
	}

	public class LevelInfo
	{
		public LevelInfo()
		{
		}

		public LevelInfo(NbtTag dataTag)
		{
			SetProperty(dataTag, () => Version);
			SetProperty(dataTag, () => Initialized);
			SetProperty(dataTag, () => LevelName);
			SetProperty(dataTag, () => GeneratorName);
			SetProperty(dataTag, () => GeneratorVersion);
			SetProperty(dataTag, () => GeneratorOptions);
			SetProperty(dataTag, () => RandomSeed);
			SetProperty(dataTag, () => MapFeatures);
			SetProperty(dataTag, () => LastPlayed);
			SetProperty(dataTag, () => AllowCommands);
			SetProperty(dataTag, () => Hardcore);
			SetProperty(dataTag, () => GameType);
			SetProperty(dataTag, () => Time);
			SetProperty(dataTag, () => DayTime);
			SetProperty(dataTag, () => SpawnX);
			SetProperty(dataTag, () => SpawnY);
			SetProperty(dataTag, () => SpawnZ);
			SetProperty(dataTag, () => Raining);
			SetProperty(dataTag, () => RainTime);
			SetProperty(dataTag, () => Thundering);
			SetProperty(dataTag, () => ThunderTime);
		}

		public int Version { get; private set; }
		public bool Initialized { get; private set; }
		public string LevelName { get; set; }
		public string GeneratorName { get; set; }
		public int GeneratorVersion { get; set; }
		public string GeneratorOptions { get; set; }
		public long RandomSeed { get; set; }
		public bool MapFeatures { get; set; }
		public long LastPlayed { get; set; }
		public bool AllowCommands { get; set; }
		public bool Hardcore { get; set; }
		private int GameType { get; set; }
		public long Time { get; set; }
		public long DayTime { get; set; }
		public int SpawnX { get; set; }
		public int SpawnY { get; set; }
		public int SpawnZ { get; set; }
		public bool Raining { get; set; }
		public int RainTime { get; set; }
		public bool Thundering { get; set; }
		public int ThunderTime { get; set; }

		public T SetProperty<T>(NbtTag tag, Expression<Func<T>> property)
		{
			var propertyInfo = ((MemberExpression) property.Body).Member as PropertyInfo;
			if (propertyInfo == null)
			{
				throw new ArgumentException("The lambda expression 'property' should point to a valid Property");
			}

			NbtTag nbtTag = tag[propertyInfo.Name];
			if (nbtTag == null)
			{
				nbtTag = tag[LowercaseFirst(propertyInfo.Name)];
			}

			if (nbtTag == null) return default(T);

			var mex = property.Body as MemberExpression;
			var target = Expression.Lambda(mex.Expression).Compile().DynamicInvoke();

			switch (nbtTag.TagType)
			{
				case NbtTagType.Unknown:
					break;
				case NbtTagType.End:
					break;
				case NbtTagType.Byte:
					if (propertyInfo.PropertyType == typeof (bool)) propertyInfo.SetValue(target, nbtTag.ByteValue == 1);
					else propertyInfo.SetValue(target, nbtTag.ByteValue);
					break;
				case NbtTagType.Short:
					propertyInfo.SetValue(target, nbtTag.ShortValue);
					break;
				case NbtTagType.Int:
					if (propertyInfo.PropertyType == typeof (bool)) propertyInfo.SetValue(target, nbtTag.IntValue == 1);
					else propertyInfo.SetValue(target, nbtTag.IntValue);
					break;
				case NbtTagType.Long:
					propertyInfo.SetValue(target, nbtTag.LongValue);
					break;
				case NbtTagType.Float:
					propertyInfo.SetValue(target, nbtTag.FloatValue);
					break;
				case NbtTagType.Double:
					propertyInfo.SetValue(target, nbtTag.DoubleValue);
					break;
				case NbtTagType.ByteArray:
					propertyInfo.SetValue(target, nbtTag.ByteArrayValue);
					break;
				case NbtTagType.String:
					propertyInfo.SetValue(target, nbtTag.StringValue);
					break;
				case NbtTagType.List:
					break;
				case NbtTagType.Compound:
					break;
				case NbtTagType.IntArray:
					propertyInfo.SetValue(target, nbtTag.IntArrayValue);
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			return (T) propertyInfo.GetValue(target);
		}

		private static string LowercaseFirst(string s)
		{
			// Check for empty string.
			if (string.IsNullOrEmpty(s))
			{
				return string.Empty;
			}
			// Return char and concat substring.
			return char.ToLower(s[0]) + s.Substring(1);
		}
	}
}