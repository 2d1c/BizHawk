﻿using System;

using BizHawk.Common;
using BizHawk.Common.NumberExtensions;

namespace BizHawk.Emulation.Cores.Components
{
	public sealed class SN76489sms
	{
		public int current_sample_L;
		public int current_sample_R;

		public SN76489sms()
		{
			Reset();
		}

		public byte[] Chan_vol = new byte[4];
		public ushort[] Chan_tone = new ushort[4];

		public int chan_sel;
		public bool vol_tone;
		public bool noise_type;
		public int noise_rate;
		public bool noise_bit;

		public bool A_L, B_L, C_L, noise_L;
		public bool A_R, B_R, C_R, noise_R;

		private int psg_clock;

		private int clock_A, clock_B, clock_C;

		private bool A_up, B_up, C_up;

		private int noise_clock;
		private int noise;

		public byte stereo_panning;
		private static readonly byte[] LogScale = { 255, 203, 161, 128, 102, 86, 64, 51, 40, 32, 26, 20, 16, 13, 10, 0 };

		public void Reset()
		{
			clock_A = clock_B = clock_C = 0x1000;
			noise_clock = 0x10;
			chan_sel = 0;

			// reset the shift register
			noise = 0x40000;

			Chan_vol[0] = 0xF;
			Chan_vol[1] = 0xF;
			Chan_vol[2] = 0xF;
			Chan_vol[3] = 0xF;

			Set_Panning(0xFF);
		}

		public void Set_Panning(byte value)
		{
			A_L = (value & 0x10) != 0;
			A_R = (value & 0x01) != 0;
			B_L = (value & 0x20) != 0;
			B_R = (value & 0x02) != 0;
			C_L = (value & 0x40) != 0;
			C_R = (value & 0x04) != 0;
			noise_L = (value & 0x80) != 0;
			noise_R = (value & 0x08) != 0;

			stereo_panning = value;
		}

		public void SyncState(Serializer ser)
		{
			ser.BeginSection("SN76489");

			ser.Sync("Chan_vol", ref Chan_vol, false);
			ser.Sync("Chan_tone", ref Chan_tone, false);

			ser.Sync("Chan_sel", ref chan_sel);
			ser.Sync("vol_tone", ref vol_tone);
			ser.Sync("noise_type", ref noise_type);
			ser.Sync("noise_rate", ref noise_rate);

			ser.Sync("Clock_A", ref clock_A);
			ser.Sync("Clock_B", ref clock_B);
			ser.Sync("Clock_C", ref clock_C);
			ser.Sync("noise_clock", ref noise_clock);
			ser.Sync("noise_bit", ref noise_bit);

			ser.Sync("psg_clock", ref psg_clock);

			ser.Sync("A_up", ref A_up);
			ser.Sync("B_up", ref B_up);
			ser.Sync("C_up", ref C_up);
			ser.Sync("noise", ref noise);

			ser.Sync("A_L", ref A_L);
			ser.Sync("B_L", ref B_L);
			ser.Sync("C_L", ref C_L);
			ser.Sync("noise_L", ref noise_L);
			ser.Sync("A_L", ref A_R);
			ser.Sync("B_L", ref B_R);
			ser.Sync("C_L", ref C_R);
			ser.Sync("noise_L", ref noise_R);

			ser.Sync("current_sample_L", ref current_sample_L);
			ser.Sync("current_sample_R", ref current_sample_R);
			ser.Sync("stereo_panning", ref stereo_panning);

			ser.EndSection();
		}

		public byte ReadReg()
		{
			// not used, reading not allowed, just return 0xFF
			return 0xFF;
		}

		public void WriteReg(byte value)
		{
			// if bit 7 is set, change the latch, otherwise modify the currently latched register
			if (value.Bit(7))
			{
				chan_sel = (value >> 5) & 3;
				vol_tone = value.Bit(4);

				if (vol_tone)
				{
					Chan_vol[chan_sel] = (byte)(value & 0xF);
				}
				else
				{
					if (chan_sel < 3)
					{
						Chan_tone[chan_sel] &= 0x3F0;
						Chan_tone[chan_sel] |= (ushort)(value & 0xF);
					}
					else
					{
						noise_type = value.Bit(2);
						noise_rate = value & 3;

						// reset the shift register
						noise = 0x40000;
					}
				}
			}
			else
			{
				if (vol_tone)
				{
					Chan_vol[chan_sel] = (byte)(value & 0xF);
				}
				else
				{
					if (chan_sel < 3)
					{
						Chan_tone[chan_sel] &= 0xF;
						Chan_tone[chan_sel] |= (ushort)((value & 0x3F) << 4);
					}
					else
					{
						noise_type = value.Bit(2);
						noise_rate = value & 3;

						// reset the shift register
						noise = 0x40000;
					}
				}
			}
		}

		public void generate_sound()
		{
			// there are 16 cpu cycles for every psg cycle
			psg_clock++;

			if (psg_clock == 16)
			{
				psg_clock = 0;

				clock_A--;
				clock_B--;
				clock_C--;
				noise_clock--;

				// clock noise
				if (noise_clock == 0)
				{
					noise_bit = noise.Bit(0);
					if (noise_type)
					{
						noise = (((noise & 1) ^ ((noise >> 1) & 1)) << 14) | (noise >> 1);						
					}
					else
					{
						noise = ((noise & 1) << 14) | (noise >> 1);
					}

					if (noise_rate == 0)
					{
						noise_clock = 0x10;
					}
					else if (noise_rate == 1)
					{
						noise_clock = 0x20;
					}
					else if (noise_rate == 2)
					{
						noise_clock = 0x40;
					}
					else
					{
						noise_clock = Chan_tone[2] + 1;
					}

					noise_clock *= 2;					
				}
					
				if (clock_A == 0)
				{
					A_up = !A_up;
					clock_A = Chan_tone[0] + 1;
				}

				if (clock_B == 0)
				{
					B_up = !B_up;
					clock_B = Chan_tone[1] + 1;
				}

				if (clock_C == 0)
				{
					C_up = !C_up;
					clock_C = Chan_tone[2] + 1;
				}

				// now calculate the volume of each channel and add them together
				current_sample_L = (A_L ? (A_up ? LogScale[Chan_vol[0]] * 42 : 0) : 0);

				current_sample_L += (B_L ? (B_up ? LogScale[Chan_vol[1]] * 42 : 0) : 0);

				current_sample_L += (C_L ? (C_up ? LogScale[Chan_vol[2]] * 42 : 0) : 0);

				current_sample_L += (noise_L ? (noise_bit ? LogScale[Chan_vol[3]] * 42 : 0) : 0);

				current_sample_R = (A_R ? (A_up ? LogScale[Chan_vol[0]] * 42 : 0) : 0);

				current_sample_R += (B_R ? (B_up ? LogScale[Chan_vol[1]] * 42 : 0) : 0);

				current_sample_R += (C_R ? (C_up ? LogScale[Chan_vol[2]] * 42 : 0) : 0);

				current_sample_R += (noise_R ? (noise_bit ? LogScale[Chan_vol[3]] * 42 : 0) : 0);
			}
		}
	}
}