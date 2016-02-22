﻿using BizHawk.Emulation.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BizHawk.Emulation.Common.IEmulatorExtensions;
using BizHawk.Common.NumberExtensions;

namespace BizHawk.Emulation.Cores.Consoles.Sega.gpgx
{
	public partial class GPGX
	{
		private readonly ITraceable Tracer;

		// TODO: move this to BaseImplementations and make the TraceFromCallback settable by the core
		public class CallbackBasedTraceBuffer : ITraceable
		{
			public CallbackBasedTraceBuffer(IDebuggable debuggableCore)
			{
				if (!debuggableCore.MemoryCallbacksAvailable())
				{
					throw new InvalidOperationException("Memory callbacks are required");
				}

				try
				{
					var dummy = debuggableCore.GetCpuFlagsAndRegisters();
				}
				catch(NotImplementedException)
				{
					throw new InvalidOperationException("GetCpuFlagsAndRegisters is required");
				}

				Header = "Instructions";
				DebuggableCore = debuggableCore;

				// TODO: refactor
				MemoryDomains = (debuggableCore as IEmulator).ServiceProvider.GetService<IMemoryDomains>();
				Disassembler = (debuggableCore as IEmulator).ServiceProvider.GetService<IDisassemblable>();
			}

			// TODO: think about this
			private readonly IMemoryDomains MemoryDomains;
			private readonly IDisassemblable Disassembler;
			private readonly IDebuggable DebuggableCore;

			private readonly List<TraceInfo> Buffer = new List<TraceInfo>();

			private bool _enabled;

			private void TraceFromCallback()
			{
				var regs = DebuggableCore.GetCpuFlagsAndRegisters();
				uint pc = (uint)regs["M68K PC"].Value;
				var length = 0;
				var disasm = Disassembler.Disassemble(MemoryDomains.SystemBus, pc, out length);

				var traceInfo = new TraceInfo
				{
					Disassembly = string.Format("{0:X6}:  {1,-32}", pc, disasm)
				};

				var sb = new StringBuilder();

				foreach (var r in regs)
				{
					if (r.Key.StartsWith("M68K")) // drop Z80 regs until it has its own debugger/tracer
					{
						if (r.Key != "M68K SP" && r.Key != "M68K ISP" && // copies of a7
							r.Key != "M68K PC" && // already present in every line start
							r.Key != "M68K IR") // copy of last opcode, already shown in raw bytes
						{
							sb.Append(
								string.Format("{0}:{1} ",
								r.Key.Replace("M68K", string.Empty).Trim(),
								r.Value.Value.ToHexString(r.Value.BitSize / 4)));
						}
					}
				}
				var sr = regs["M68K SR"].Value;
				sb.Append(
					string.Format("{0}{1}{2}{3}{4}",
					(sr & 16) > 0 ? "X" : "x",
					(sr &  8) > 0 ? "N" : "n",
					(sr &  4) > 0 ? "Z" : "z",
					(sr &  2) > 0 ? "V" : "v",
					(sr &  1) > 0 ? "C" : "c"));

				traceInfo.RegisterInfo = sb.ToString().Trim();

				Buffer.Add(traceInfo);
			}

			public bool Enabled
			{
				get
				{
					return _enabled;
				}

				set
				{
					_enabled = value;
					DebuggableCore.MemoryCallbacks.Remove(TraceFromCallback);

					if (_enabled)
					{
						DebuggableCore.MemoryCallbacks.Add(new TracingMemoryCallback(TraceFromCallback));
					}
				}
			}

			public string Header { get; set; }

			public IEnumerable<TraceInfo> Contents
			{
				get { return Buffer; }
			}

			public IEnumerable<TraceInfo> TakeContents()
			{
				var contents = Buffer.ToList();
				Buffer.Clear();
				return contents;
			}

			public void Put(TraceInfo content)
			{
				if (Enabled)
				{
					Buffer.Add(content);
				}
			}

			public class TracingMemoryCallback : IMemoryCallback
			{
				public TracingMemoryCallback(Action callback)
				{
					Callback = callback;
				}

				public MemoryCallbackType Type
				{
					get { return MemoryCallbackType.Execute; }
				}

				public string Name
				{
					get { return "Trace Logging"; }
				}

				public Action Callback { get; private set; }

				public uint? Address
				{
					get { return null; }
				}
			}
		}
	}
}