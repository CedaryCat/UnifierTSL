/*
TShock, a server mod for Terraria
Copyright (C) 2011-2025 Pryaxis & TShock Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Net;
using System.Net.Sockets;
using TShockAPI.Configuration;

namespace TShockAPI;

/// <summary>
/// Provides storage and checks for whitelist entries.
/// </summary>
public sealed class Whitelist
{
    private readonly FileInfo file;
    private readonly object fileLock = new();

    private readonly HashSet<IPAddress> whitelistAddresses = [];
    private readonly HashSet<IPNetwork> whitelistNetworks = [];

    /// <summary>
    /// Defines if the whitelist is enabled or not.
    /// </summary>
    /// <remarks>Shorthand to the current <see cref="TShockSettings.EnableWhitelist"/> setting.</remarks>
    private bool Enabled => TShock.Config.GlobalSettings.EnableWhitelist;

    internal const string DefaultWhitelistContent =
        "# Localhost\n" +
        "127.0.0.1\n" +
        "\n" +
        "# Uncomment to allow IPs within private ranges\n" +
        "# 10.0.0.0/8\n" +
        "# 172.16.0.0/12\n" +
        "# 192.168.0.0/16\n";

    internal const char CommentPrefix = '#';

    /// <summary>
    /// Initializes a new instance of the <see cref="Whitelist"/> class.
    /// </summary>
    /// <param name="path">The whitelist file path.</param>
    public Whitelist(string path)
    {
        file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The whitelist file does not exist.", file.FullName);
        }

        ReadWhitelistFromFile();
    }

    /// <summary>
    /// Returns whether the host is whitelisted.
    /// </summary>
    /// <param name="host">IP address string of the user.</param>
    public bool IsWhitelisted(string host)
    {
        if (!Enabled)
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out IPAddress? ip))
        {
            TShock.Log.Warning(GetString($"The provided host '{host}' is not a valid IP address."));
            return false;
        }

        // Terraria currently only supports IPv4.
        if (ip.AddressFamily is AddressFamily.InterNetworkV6)
        {
            TShock.Log.Warning(GetString($"IPv6 address '{ip}' is not supported by Terraria. Skipping whitelist check."));
            TShock.Log.Warning(GetString("Please report this to the TShock team if needed: https://github.com/Pryaxis/TShock/issues"));
            return false;
        }

        lock (fileLock)
        {
            return whitelistAddresses.Contains(ip) || whitelistNetworks.Any(network => network.Contains(ip));
        }
    }

    /// <summary>
    /// Reloads all whitelist entries from file.
    /// </summary>
    public void ReloadFromFile()
    {
        lock (fileLock)
        {
            whitelistAddresses.Clear();
            whitelistNetworks.Clear();
            ReadWhitelistFromFile();
        }
    }

    /// <summary>
    /// Adds an IP address or network to the whitelist.
    /// </summary>
    public bool AddToWhitelist(ReadOnlySpan<char> ip)
    {
        if (IPNetwork.TryParse(ip, out IPNetwork range))
        {
            return AddToWhitelist(range);
        }

        if (IPAddress.TryParse(ip, out IPAddress? address))
        {
            return AddToWhitelist(address);
        }

        return false;
    }

    /// <summary>
    /// Removes an IP address or network from the whitelist.
    /// </summary>
    public bool RemoveFromWhitelist(ReadOnlySpan<char> ip)
    {
        if (IPNetwork.TryParse(ip, out IPNetwork range))
        {
            return RemoveFromWhitelist(range);
        }

        if (IPAddress.TryParse(ip, out IPAddress? address))
        {
            return RemoveFromWhitelist(address);
        }

        return false;
    }

    private void ReadWhitelistFromFile()
    {
        using StreamReader reader = file.OpenText();

        int lineIndex = 1;
        while (!reader.EndOfStream)
        {
            ReadWhitelistLine(reader.ReadLine(), lineIndex);
            lineIndex++;
        }
    }

    private void ReadWhitelistLine(string? content, int lineIndex)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var line = content.Trim();
        if (line.Length == 0 || line[0] == CommentPrefix)
        {
            return;
        }

        if (IPNetwork.TryParse(line, out IPNetwork range))
        {
            whitelistNetworks.Add(range);
            return;
        }

        if (IPAddress.TryParse(line, out IPAddress? ip))
        {
            whitelistAddresses.Add(ip);
            return;
        }

        TShock.Log.Warning(GetString($"Invalid whitelist entry at line {lineIndex}: \"{line}\", skipped"));
    }

    private bool AddToWhitelist(IPAddress ip)
        => whitelistAddresses.Add(ip) & AddLine(ip.ToString());

    private bool AddToWhitelist(IPNetwork network)
        => whitelistNetworks.Add(network) & AddLine(network.ToString());

    private bool RemoveFromWhitelist(IPAddress ip)
        => whitelistAddresses.Remove(ip) & RemoveLine(ip.ToString());

    private bool RemoveFromWhitelist(IPNetwork network)
        => whitelistNetworks.Remove(network) & RemoveLine(network.ToString());

    private bool AddLine(ReadOnlySpan<char> content)
    {
        lock (fileLock)
        {
            bool needsNewLine = false;
            using (FileStream fs = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length > 0)
                {
                    fs.Seek(-1, SeekOrigin.End);
                    needsNewLine = fs.ReadByte() is not '\n';
                }
            }

            using StreamWriter writer = file.AppendText();
            if (needsNewLine)
            {
                writer.WriteLine();
            }

            writer.WriteLine(content);
            return true;
        }
    }

    private bool RemoveLine(ReadOnlySpan<char> content)
    {
        if (content.IsEmpty)
        {
            throw new ArgumentException("Content cannot be empty.", nameof(content));
        }

        lock (fileLock)
        {
            string tempFile = Path.GetTempFileName();
            bool removed = false;

            using (StreamReader reader = file.OpenText())
            using (StreamWriter writer = new StreamWriter(tempFile))
            {
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (line is null || !line.AsSpan().SequenceEqual(content))
                    {
                        writer.WriteLine(line);
                    }
                    else
                    {
                        removed = true;
                    }
                }
            }

            if (removed)
            {
                file.Delete();
                File.Move(tempFile, file.FullName);
            }
            else
            {
                File.Delete(tempFile);
            }

            return removed;
        }
    }
}
