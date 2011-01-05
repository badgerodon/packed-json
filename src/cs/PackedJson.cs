using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Badgerodon
{
	public class PackedJson
	{
		[AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
		public sealed class IgnoreAttribute : Attribute
		{
			public IgnoreAttribute()
			{

			}
		}

		public class BinaryBuffer
		{
			private byte[] _buffer;
			private int _pos;

			public BinaryBuffer(byte[] data)
			{
				_buffer = data;
				_pos = 0;
			}
			public byte Read()
			{
				return _buffer[_pos++];
			}
			public byte[] Read(ulong n)
			{
				var arr = new byte[n];
				for (ulong i = 0; i < n; i++)
				{
					arr[i] = Read();
				}
				return arr;
			}
		}

		private static bool HasAttribute(PropertyInfo pi, Type attribute)
		{
			if (pi.GetCustomAttributes(attribute, false).Length > 0)
			{
				return true;
			}
			var p = pi.ReflectedType.BaseType;
			if (p != null && p != pi.ReflectedType)
			{
				var pi2 = p.GetProperty(pi.Name);
				if (pi2 != null)
				{
					return HasAttribute(pi2, attribute);
				}
			}
			return false;
		}

		private static Action<object, List<byte>> GetConverter(object obj)
		{
			var t = obj.GetType();
			var m = t.GetMethod("ToPackedJson");
			if (m != null)
			{
				return (o, bytes) =>
				{
					m.Invoke(o, new object[] { bytes });
				};
			}
			else
			{
				var properties = t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
					.Where(pi => !HasAttribute(pi, typeof(IgnoreAttribute)))
					.ToList();
				var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public)
					.ToList();

				return (o, bytes) =>
				{
					var d = new Dictionary<string, object>();
					foreach (var pi in properties)
					{
						d[pi.Name] = pi.GetValue(o, new object[] { });
					}
					foreach (var fi in fields)
					{
						d[fi.Name] = fi.GetValue(o);
					}
					PackValue(d, bytes);
				};
			}
		}

		private static long PowerOf2(int n)
		{
			long x = 1;
			for (int i = 0; i < n; i++)
			{
				x *= 2;
			}
			return x;
		}

		private static ulong PowerOf2U(int n)
		{
			ulong x = 1;
			for (int i = 0; i < n; i++)
			{
				x *= 2;
			}
			return x;
		}

		private static bool[] ByteToBits(byte b)
		{
			var arr = new bool[8];
			for (var i = 0; i < 8; i++)
			{
				arr[7-i] = ((b >> i) & 0x01) != 0
					? true
					: false;
			}
			return arr;
		}

		public static void PackByte(byte b, List<byte> bytes)
		{
			bytes.Add(b);
		}

		public static void PackChar(char c, List<byte> bytes)
		{
			PackVariableUnsignedInt((uint)c, bytes);
		}

		public static void PackDateTime(DateTime d, List<byte> bytes)
		{
			PackVariableInt(d.ToBinary(), bytes);
		}

		public static void PackFixed(float n, List<byte> bytes)
		{
			var scale = 100000000f;
			n *= scale;
			PackVariableInt((long)Math.Round(n), bytes);
		}

		public static void PackFixed(double n, List<byte> bytes)
		{
			var scale = 10000000000000000d;
			n *= scale;
			PackVariableInt((long)Math.Round(n), bytes);
		}

		private static void PackVariable(BitArray bits, List<byte> bytes)
		{
			int last = 0;
			for (int i = 0; i < bits.Count; i++)
			{
				if (bits[i])
				{
					last = i;
				}
			}

			List<byte> newbytes = new List<byte>();
			for (int i = 0; i < last + 1; i++)
			{
				var bpos = 7 - i % 7;
				var pos = i / 7;
				if (newbytes.Count <= pos) newbytes.Add(0x80);

				if (bits[i])
				{
					newbytes[pos] = (byte)(newbytes[pos] | (1 << (bpos-1)));
				}
			}

			newbytes[newbytes.Count - 1] = (byte)(newbytes[newbytes.Count - 1] & 0x7F);

			bytes.AddRange(newbytes);
		}

		public static void PackVariableUnsignedInt(ulong n, List<byte> bytes)
		{
			var bits = new BitArray(BitConverter.GetBytes(n));
			PackVariable(bits, bytes);
		}

		public static void PackVariableInt(long n, List<byte> bytes)
		{
			var temp = BitConverter.GetBytes(Math.Abs(n));
			BitArray bits = new BitArray(temp);
			BitArray withSign = new BitArray(bits.Length + 1);
			withSign[0] = n < 0 ? true : false;
			for (int i = 0; i < bits.Length; i++)
			{
				withSign[i + 1] = bits[i];
			}
			PackVariable(withSign, bytes);
		}

		public static void PackObject(Dictionary<string, object> obj, List<byte> bytes)
		{
			PackVariableUnsignedInt((ulong)obj.Count, bytes);
			foreach (var kvp in obj)
			{
				PackString(kvp.Key, bytes);
				PackValue(kvp.Value, bytes);
			}
		}

		public static void PackArray(List<object> obj, List<byte> bytes)
		{
			PackVariableUnsignedInt((ulong)obj.Count, bytes);
			foreach (var element in obj)
			{
				PackValue(element, bytes);
			}
		}

		public static void PackString(string str, List<byte> bytes)
		{
			var temp = Encoding.UTF8.GetBytes(str);
			PackVariableUnsignedInt((ulong)temp.Length, bytes);
			bytes.AddRange(temp);
		}

		public static void PackBinary(byte[] binary, List<byte> bytes)
		{
			PackVariableUnsignedInt((ulong)binary.Length, bytes);
			bytes.AddRange(binary);
		}
		public static void PackValue(object obj, List<byte> bytes)
		{
			if (obj == null)
			{
				bytes.Add(0x00); // Null (0)
			}
			else
			{
				switch (Type.GetTypeCode(obj.GetType()))
				{
					case TypeCode.Boolean:
						{
							if ((bool)obj)
							{
								bytes.Add(0x01); // True (0)
							}
							else
							{
								bytes.Add(0x02); // False (0)
							}
							break;
						}
					case TypeCode.Byte:
						{
							bytes.Add(0x03); // Byte (1)
							PackByte((byte)obj, bytes);
							break;
						}
					case TypeCode.Char:
						{
							bytes.Add(0x04); // Char (2)
							PackChar((char)obj, bytes);
							break;
						}
					case TypeCode.DateTime:
						{
							bytes.Add(0x05); // DateTime (8)
							PackDateTime((DateTime)obj, bytes);
							break;
						}
					case TypeCode.DBNull:
						bytes.Add(0x00); break;
					case TypeCode.Decimal:
						bytes.Add(0x11);
						PackFixed((double)(decimal)obj, bytes);
						break;
					case TypeCode.Double:
						bytes.Add(0x11);
						PackFixed((double)obj, bytes);
						break;
					case TypeCode.Empty:
						bytes.Add(0x00); break;
					case TypeCode.Int16:
						bytes.Add(0x13);
						PackVariableInt((short)obj, bytes);
						break;
					case TypeCode.Int32:
						bytes.Add(0x13);
						PackVariableInt((int)obj, bytes);
						break;
					case TypeCode.Int64:
						bytes.Add(0x13);
						PackVariableInt((long)obj, bytes);
						break;
					case TypeCode.Object:
					{
						if (obj is byte[])
						{
							bytes.Add(0x23);
							PackBinary((byte[])obj, bytes);
						}
						else if (obj is IDictionary)
						{
							bytes.Add(0x20);
							Dictionary<string, object> dictionary = new Dictionary<string, object>();
							foreach (DictionaryEntry entry in (IDictionary)obj)
							{
								var key = Convert.ToString(entry.Key);
								dictionary[key] = entry.Value;
							}
							PackObject(dictionary, bytes);
						}
						else if (obj is IEnumerable)
						{
							bytes.Add(0x21);
							var list = new List<object>();
							foreach (object el in (IEnumerable)obj)
							{
								list.Add(el);
							}
							PackArray(list, bytes);
						}
						else
						{
							GetConverter(obj)(obj, bytes);
						}
						break;
					}
					case TypeCode.SByte:
						bytes.Add(0x13);
						PackVariableInt((sbyte)obj, bytes);
						break;
					case TypeCode.Single:
						bytes.Add(0x10);
						PackFixed((float)obj, bytes);
						break;
					case TypeCode.String:
						bytes.Add(0x22);
						PackString((string)obj, bytes);
						break;
					case TypeCode.UInt16:
						bytes.Add(0x12);
						PackVariableUnsignedInt((ushort)obj, bytes);
						break;
					case TypeCode.UInt32:
						bytes.Add(0x12);
						PackVariableUnsignedInt((uint)obj, bytes);
						break;
					case TypeCode.UInt64:
						bytes.Add(0x12);
						PackVariableUnsignedInt((ulong)obj, bytes);
						break;
				}
			}
		}

		public static byte[] Pack(object obj)
		{
			List<byte> bytes = new List<byte>();
			PackValue(obj, bytes);
			return bytes.ToArray();
		}

		public static byte UnpackByte(BinaryBuffer buffer)
		{
			return buffer.Read();
		}

		public static char UnpackChar(BinaryBuffer buffer)
		{
			var i = UnpackVariableUnsignedInt(buffer);
			return (char)i;
		}

		public static DateTime UnpackDateTime(BinaryBuffer buffer)
		{
			return DateTime.FromBinary(UnpackVariableInt(buffer));
		}

		public static float UnpackFixedSingle(BinaryBuffer buffer)
		{
			var n = UnpackVariableInt(buffer);
			return (float) (((double)n) / 100000000d);
		}
		public static double UnpackFixedDouble(BinaryBuffer buffer)
		{
			var n = UnpackVariableInt(buffer);
			return (((double)n) / 10000000000000000d);
		}

		public static ulong UnpackVariableUnsignedInt(BinaryBuffer buffer)
		{
			List<bool> bits = new List<bool>();
			while (true)
			{
				var b = buffer.Read();
				var ba = ByteToBits(b);

				for (var i = 1; i < ba.Length; i++)
				{
					bits.Add(ba[i]);
				}

				// Last byte
				if (!ba[0])
				{
					break;
				}
			}

			ulong n = 0;
			for (int i = 0; i < bits.Count; i++)
			{
				if (bits[i])
				{
					n += PowerOf2U(i);
				}
			}
			return n;
		}

		private static long UnpackVariableInt(BinaryBuffer buffer)
		{
			List<bool> bits = new List<bool>();
			bool? positive = null;
			while (true)
			{
				var b = buffer.Read();
				var ba = ByteToBits(b);
				for (var i = 1; i < ba.Length; i++)
				{
					if (!positive.HasValue)
					{
						positive = !ba[i];
					} 
					else
					{
						bits.Add(ba[i]);
					}
				}

				// Last byte
				if (!ba[0])
				{
					break;
				}
			}

			long n = 0;
			for (int i = 0; i < bits.Count; i++)
			{
				if (bits[i])
				{
					n += PowerOf2(i);
				}
			}
			return positive.Value ? n : -n;
		}

		public static Dictionary<string, object> UnpackObject(BinaryBuffer buffer)
		{
			var len = UnpackVariableUnsignedInt(buffer);
			var obj = new Dictionary<string, object>();
			for (ulong i = 0; i < len; i++)
			{
				var key = UnpackString(buffer);
				var value = UnpackValue(buffer);
				obj[key] = value;
			}
			return obj;
		}

		public static object[] UnpackArray(BinaryBuffer buffer)
		{
			var len = UnpackVariableUnsignedInt(buffer);
			var arr = new object[len];
			for (ulong i = 0; i < len; i++)
			{
				var value = UnpackValue(buffer);
				arr[i] = value;
			}
			return arr;
		}

		public static string UnpackString(BinaryBuffer buffer)
		{
			var len = UnpackVariableUnsignedInt(buffer);
			var bytes = buffer.Read(len);
			return Encoding.UTF8.GetString(bytes);
		}

		public static byte[] UnpackBinary(BinaryBuffer buffer)
		{
			var len = UnpackVariableUnsignedInt(buffer);
			var bytes = buffer.Read(len);
			return bytes;
		}

		public static object UnpackValue(BinaryBuffer buffer)
		{
			var type = buffer.Read();
			switch (type)
			{
				case 0x00: return null;
				case 0x01: return true;
				case 0x02: return false;
				// Basic types
				case 0x03: return UnpackByte(buffer);
				case 0x04: return UnpackChar(buffer);
				case 0x05: return UnpackDateTime(buffer);
				case 0x06: throw new NotImplementedException();
				case 0x07: throw new NotImplementedException();
				case 0x08: throw new NotImplementedException();
				case 0x09: throw new NotImplementedException();
				case 0x0A: throw new NotImplementedException();
				case 0x0B: throw new NotImplementedException();
				case 0x0C: throw new NotImplementedException();
				case 0x0D: throw new NotImplementedException();
				case 0x0E: throw new NotImplementedException();
				case 0x0F: throw new NotImplementedException();
				// - Custom
				case 0x10: return UnpackFixedSingle(buffer);
				case 0x11: return UnpackFixedDouble(buffer);
				case 0x12: return UnpackVariableUnsignedInt(buffer);
				case 0x13: return UnpackVariableInt(buffer);
				// - Complex
				case 0x20: return UnpackObject(buffer);
				case 0x21: return UnpackArray(buffer);
				case 0x22: return UnpackString(buffer);
				case 0x23: return UnpackBinary(buffer);
			}
			return null;
		}

		public static object Unpack(byte[] bytes)
		{
			return UnpackValue(new BinaryBuffer(bytes));
		}

		public static T Unpack<T>(byte[] bytes)
		{
			return (T)UnpackValue(new BinaryBuffer(bytes));
		}
	}
}
