using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Autocorrect.Cli.Brain;

internal static class BrainPortResolver
{
  // Reuses an existing Woody brain server or finds the next free localhost port.
  public static async Task<BrainPortResolution> ResolveAsync(int preferredPort, bool openBrowser, CancellationToken cancellationToken)
  {
    if (await IsWoodyBrainRunningAsync(preferredPort, cancellationToken))
    {
      var url = $"http://127.0.0.1:{preferredPort}";
      Console.WriteLine($"Brain already running at {url}");
      if (openBrowser)
      {
        TryOpenBrowser(url);
      }

      return new BrainPortResolution(url, AlreadyRunning: true);
    }

    if (IsPortFree(preferredPort))
    {
      return new BrainPortResolution($"http://127.0.0.1:{preferredPort}", AlreadyRunning: false, preferredPort);
    }

    for (var port = preferredPort + 1; port <= preferredPort + 20; port++)
    {
      if (!IsPortFree(port))
      {
        continue;
      }

      Console.WriteLine($"Port {preferredPort} is busy. Using {port} instead.");
      return new BrainPortResolution($"http://127.0.0.1:{port}", AlreadyRunning: false, port);
    }

    throw new InvalidOperationException($"No free port found near {preferredPort}. Stop the other process or pass --port.");
  }

  private static async Task<bool> IsWoodyBrainRunningAsync(int port, CancellationToken cancellationToken)
  {
    try
    {
      using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
      using var response = await client.GetAsync($"http://127.0.0.1:{port}/api/status", cancellationToken);
      return response.IsSuccessStatusCode;
    }
    catch
    {
      return false;
    }
  }

  private static bool IsPortFree(int port)
  {
    try
    {
      using var listener = new TcpListener(IPAddress.Loopback, port);
      listener.Start();
      listener.Stop();
      return true;
    }
    catch (SocketException)
    {
      return false;
    }
  }

  private static void TryOpenBrowser(string url)
  {
    try
    {
      Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch
    {
      Console.WriteLine($"Open in browser: {url}");
    }
  }
}

internal readonly record struct BrainPortResolution(string Url, bool AlreadyRunning, int Port = 0);
