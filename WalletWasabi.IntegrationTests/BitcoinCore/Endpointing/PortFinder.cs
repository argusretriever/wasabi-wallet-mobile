using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NBitcoin;
using WalletWasabi.Logging;

namespace WalletWasabi.IntegrationTests.BitcoinCore.Endpointing;

public static class PortFinder
{
	private static readonly HashSet<int> AllocatedPorts = [];

	public static int[] GetRandomPorts(int num)
	{
		const int firstPort = 10000;
		const int portCount = 4000;
		var ports = new int[num];
		var start = (int)(RandomUtils.GetUInt32() % portCount);
		lock (AllocatedPorts)
		{
			var found = 0;
			for (var offset = 0; offset < portCount && found < ports.Length; offset++)
			{
				var port = firstPort + (start + offset) % portCount;
				if (AllocatedPorts.Contains(port))
				{
					continue;
				}

				try
				{
					using var listener = new TcpListener(IPAddress.Loopback, port);
					listener.Start();
					ports[found++] = port;
				}
				catch (SocketException ex)
				{
					Logger.LogTrace(ex);
				}
			}

			if (found != ports.Length)
			{
				throw new InvalidOperationException("Not enough unused Bitcoin Core test ports are available.");
			}

			AllocatedPorts.UnionWith(ports);
		}

		return ports;
	}
}
