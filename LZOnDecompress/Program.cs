using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LZOnDecompress {
	class Program {
		static int Main(string[] args) {
			bool success = false;

			try {
				Console.WriteLine("LZOnDecompress v0.1 by Prof. 9");
				Console.WriteLine();

				if (args.Length != 1) {
					Console.WriteLine("Usage:");
					Console.WriteLine("    LZOnDecompress.exe <filename>");
#if DEBUG
					Console.ReadKey();
#endif
				} else {

					MemoryStream input = new MemoryStream();
					using (FileStream fs = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read)) {
						fs.CopyTo(input);
					}

					MemoryStream output = new MemoryStream();
					input.Position = 0;
					try {
						success = Decompress(input, output);
					} catch { }

					if (success) {
						Console.WriteLine("File decompressed successfully.");
						string outPath = Path.GetFileNameWithoutExtension(args[0]) + ".srl";
						output.Position = 0;
						using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
							output.CopyTo(fs);
						}
					} else {
						Console.WriteLine("Could not decompress file.");
					}
				}

				Console.WriteLine();
				Console.WriteLine("Done.");
			} catch (Exception ex) {
				Console.WriteLine("FATAL: " + ex.Message);
				return 2;
			}
#if DEBUG
			Console.ReadKey(true);
#endif

			return success ? 0 : 1;
		}

		static bool Decompress(MemoryStream input, MemoryStream output) {
			int state, len, dist, instr;

			try {
				// Check header magic.
				if (input.ReadByte() != 'L' ||
					input.ReadByte() != 'Z' ||
					input.ReadByte() != 'O' ||
					input.ReadByte() != 'n' ||
					input.ReadByte() != 0x00 ||
					input.ReadByte() != 0x2F ||
					input.ReadByte() != 0xF1 ||
					input.ReadByte() != 0x71) {
					return false;
				}

				// Read sizes.
				uint decompressedSize = InputRead32();
				uint compressedSize = InputRead32() + 17;
				bool hasInstr = false;
				state = -1;

				// Process first byte.
				instr = InputRead8();
				if (instr > 17) {
					len = instr - 17;
					InputCopy();
					state = Math.Min(instr - 17, 4);
				} else {
					hasInstr = true;
				}

				while (true) {
					if (input.Position >= compressedSize) {
						return false;
					}
					if (!hasInstr) {
						instr = InputRead8();
					}
					hasInstr = false;

					if (instr <= 0xF) {
						switch (state) {
						case 0:
							len = ReadLen(0, 4) + 3;
							state = 4;
							InputCopy();
							break;
						case 1:
						case 2:
						case 3:
							len = 2;
							dist = ReadOper(2, 2) + 1 + (InputRead8() << 2);
							state = ReadOper(0, 2);
							DictionaryCopy();
							break;
						case 4:
							len = 3;
							dist = ReadOper(2, 2) + 2049 + (InputRead8() << 2);
							state = ReadOper(0, 2);
							DictionaryCopy();
							break;
						default:
							return false;
						}
					} else if (instr <= 0x1F) {
						len = ReadLen(0, 3) + 2;
						int x = InputRead16();
						dist = (ReadOper(3, 1) << 14) + 16384 + (x >> 2);
						if (dist == 16384) {
							break;
						}
						state = x & 0b11;
						DictionaryCopy();
					} else if (instr <= 0x3F) {
						len = ReadLen(0, 5) + 2;
						int x = InputRead16();
						dist = 1 + (x >> 2);
						state = x & 0b11;
						DictionaryCopy();
					} else if (instr <= 0x7F) {
						len = ReadOper(5, 1) + 3;
						int x = InputRead8();
						dist = ReadOper(2, 3) + 1 + (x << 3);
						state = ReadOper(0, 2);
						DictionaryCopy();
					} else {
						len = ReadOper(5, 2) + 5;
						dist = ReadOper(2, 3) + 1 + (InputRead8() << 3);
						state = ReadOper(0, 2);
						DictionaryCopy();
					}

					if (state < 4) {
						len = state;
						InputCopy();
					}
				}

				while (output.Length < decompressedSize) {
					output.WriteByte(0);
				}
			} catch (EndOfStreamException) {
				return false;
			}

			return true;

			byte InputRead8() {
				int b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				return (byte)b;
			}

			ushort InputRead16() {
				ushort r = 0;

				int b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (ushort)b;

				b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (ushort)(b << 8);

				return r;
			}

			uint InputRead32() {
				uint r = 0;

				int b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (uint)(b << 24);

				b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (uint)(b << 16);

				b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (uint)(b << 8);

				b = input.ReadByte();
				if (b < 0) throw new EndOfStreamException();
				r += (uint)b;

				return r;
			}

			int ReadOper(int shift, int bits) {
				return (instr >> shift) & ((1 << bits) - 1);
			}

			int ReadLen(int shift, int bits) {
				int mask = (1 << bits) - 1;
				int r = (instr >> shift) & mask;
				if (r == 0) {
					r = mask;
					int b;
					while ((b = input.ReadByte()) == 0) {
						r += 255;
					}
					if (b < 0) throw new EndOfStreamException();
					r += b;
				}
				return r;
			}

			void InputCopy() {
				int b;
				while (len-- > 0) {
					b = input.ReadByte();
					if (b < 0) throw new EndOfStreamException();
					output.WriteByte((byte)b);
				}
			}

			void DictionaryCopy() {
				int b;
				while (len-- > 0) {
					output.Position -= dist;
					b = output.ReadByte();
					if (b < 0) throw new EndOfStreamException();
					output.Position += dist - 1;
					output.WriteByte((byte)b);
				}
			}
		}
	}
}
