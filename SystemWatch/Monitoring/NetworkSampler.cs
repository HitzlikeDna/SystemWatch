using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SystemWatch.Monitoring;

/// <summary>Datenrate eines Netzwerkadapters. Ohne Auswahl wird die aktive Internetverbindung verwendet.</summary>
internal sealed class NetworkSampler
{
    private static readonly TimeSpan AdapterRefreshInterval = TimeSpan.FromSeconds(10);

    private NetworkInterface? _adapter;
    private NetworkAdapterInfo? _adapterInfo;
    private string _resolvedForId = "\0";
    private DateTime _lastResolve = DateTime.MinValue;

    private long _lastReceived;
    private long _lastSent;
    private long _lastTimestamp;

    /// <summary>Leer = automatisch.</summary>
    public string SelectedAdapterId { get; set; } = "";

    public static IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsRelevant)
                .Select(ToInfo)
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<NetworkAdapterInfo>();
        }
    }

    public void Sample(SystemSnapshot snapshot)
    {
        string wanted = SelectedAdapterId ?? "";
        if (wanted != _resolvedForId || DateTime.UtcNow - _lastResolve > AdapterRefreshInterval)
            Resolve(wanted);

        snapshot.NetworkAdapter = _adapterInfo;
        if (_adapter == null)
            return;

        long received, sent;
        try
        {
            var stats = _adapter.GetIPStatistics();
            received = stats.BytesReceived;
            sent = stats.BytesSent;
        }
        catch (NetworkInformationException)
        {
            _adapter = null;
            _lastResolve = DateTime.MinValue;
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_lastTimestamp != 0)
        {
            double seconds = (now - _lastTimestamp) / (double)Stopwatch.Frequency;
            if (seconds > 0)
            {
                snapshot.NetworkReceiveBytesPerSecond = Math.Max(0, (received - _lastReceived) / seconds);
                snapshot.NetworkSendBytesPerSecond = Math.Max(0, (sent - _lastSent) / seconds);
            }
        }

        _lastReceived = received;
        _lastSent = sent;
        _lastTimestamp = now;
    }

    private void Resolve(string wantedId)
    {
        _lastResolve = DateTime.UtcNow;
        NetworkInterface? chosen = null;

        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            chosen = wantedId.Length > 0
                ? all.FirstOrDefault(n => n.Id == wantedId)
                : all.Where(IsRelevant)
                     .OrderByDescending(HasGateway)
                     .ThenByDescending(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                     .ThenByDescending(n => n.Speed)
                     .FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
        }

        bool changed = chosen?.Id != _adapter?.Id;
        _adapter = chosen;
        _adapterInfo = chosen == null ? null : ToInfo(chosen);
        _resolvedForId = wantedId;

        if (changed)
            _lastTimestamp = 0; // neue Basis, sonst entsteht ein Sprung
    }

    private static bool IsRelevant(NetworkInterface n) =>
        n.OperationalStatus == OperationalStatus.Up &&
        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel;

    private static bool HasGateway(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().GatewayAddresses.Any(g =>
                g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static NetworkAdapterInfo ToInfo(NetworkInterface n) => new()
    {
        Id = n.Id,
        Name = n.Name,
        Description = n.Description,
        Kind = n.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => "WLAN",
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
            NetworkInterfaceType.Ppp => "PPP",
            NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobilfunk",
            _ => "Netzwerk",
        },
        LinkSpeedBitsPerSecond = SafeSpeed(n),
    };

    private static long SafeSpeed(NetworkInterface n)
    {
        try
        {
            return Math.Max(0, n.Speed);
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }
}
