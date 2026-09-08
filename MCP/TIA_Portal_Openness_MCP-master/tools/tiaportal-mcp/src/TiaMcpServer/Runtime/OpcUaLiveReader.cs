using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Workstation.ServiceModel.Ua;
using Workstation.ServiceModel.Ua.Channels;

namespace TiaMcpServer.Runtime
{
    // Read-only live-value reader over OPC UA. This is a RUNTIME channel,
    // independent of TIA Openness. It connects to the CPU's OPC UA server
    // (default opc.tcp port 4840) with no security and an anonymous user, and reads
    // the Value attribute of the requested nodes. It never writes or calls methods.
    //
    // The session is CACHED per endpoint and REUSED across calls: opening an OPC UA
    // session involves a multi-round-trip handshake (seconds). After the first call
    // the channel stays open, so subsequent reads are fast (tens of ms). A faulted or
    // dropped session is detected and rebuilt once automatically.
    //
    // Precondition: the CPU's OPC UA server must be enabled and the variables exposed
    // in the server interface (Runtime license required on S7-1200/1500). If the
    // server is not enabled, connection is refused — reported as a clean error.

    public sealed class OpcUaReadItem
    {
        public string NodeId = "";
        public object? Value;
        public string? StatusCode;
        public string? Error;
    }

    public sealed class OpcUaReadResult
    {
        public bool Ok;
        public string? Error;
        public string Endpoint = "";
        public bool ReusedSession;   // true if an already-open cached session was used
        public List<OpcUaReadItem> Items = new List<OpcUaReadItem>();
        public long ElapsedMs;
    }

    public static class OpcUaLiveReader
    {
        private static readonly ApplicationDescription AppDescription = new ApplicationDescription
        {
            ApplicationName = "TiaMcpServer",
            ApplicationUri = "urn:TiaMcpServer:OpcUaLiveReader",
            ApplicationType = ApplicationType.Client
        };

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, ClientSessionChannel> Channels =
            new Dictionary<string, ClientSessionChannel>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, SemaphoreSlim> Locks =
            new Dictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        private static SemaphoreSlim LockFor(string endpoint)
        {
            lock (Gate)
            {
                if (!Locks.TryGetValue(endpoint, out var s)) { s = new SemaphoreSlim(1, 1); Locks[endpoint] = s; }
                return s;
            }
        }

        public static OpcUaReadResult ReadNodes(string endpointUrl, IEnumerable<string> nodeIds, int timeoutMs)
        {
            var result = new OpcUaReadResult { Endpoint = endpointUrl };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ids = nodeIds.ToList();
            int budget = timeoutMs <= 0 ? 5000 : timeoutMs;
            try
            {
                var task = ReadReuseAsync(endpointUrl, ids, result, budget);
                if (!task.Wait(budget))
                {
                    result.Error = $"OPC UA read timed out after {budget} ms (server '{endpointUrl}' unreachable or no UA server enabled).";
                    return result;
                }
            }
            catch (AggregateException ae)
            {
                result.Error = "OPC UA error: " + (ae.InnerException?.Message ?? ae.Message);
            }
            catch (Exception ex)
            {
                result.Error = "OPC UA error: " + ex.Message;
            }
            finally
            {
                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
            }
            return result;
        }

        // Close and forget all cached sessions (housekeeping; optional).
        public static void CloseAll()
        {
            List<ClientSessionChannel> chs;
            lock (Gate) { chs = Channels.Values.ToList(); Channels.Clear(); }
            foreach (var ch in chs) { try { ch.CloseAsync().Wait(2000); } catch { } }
        }

        private static async Task ReadReuseAsync(string endpointUrl, List<string> nodeIds, OpcUaReadResult result, int budget)
        {
            var gate = LockFor(endpointUrl);
            // Bound the wait on the per-endpoint lock. Without this, a prior read to an
            // unreachable/slow server keeps the lock until its own TCP timeout, and every
            // later read would queue here indefinitely. Self-cancel at the read budget so
            // waiters never pile up; the caller still gets a clean bounded error.
            if (!await gate.WaitAsync(budget))
            {
                result.Error = $"OPC UA endpoint '{endpointUrl}' is busy: a prior read is still completing. Try again shortly.";
                return;
            }
            try
            {
                // Try a reused (or freshly opened) session; if the read fails because the
                // cached session went stale, drop it and rebuild exactly once.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    var (channel, reused) = await AcquireAsync(endpointUrl, forceNew: attempt > 0);
                    try
                    {
                        await DoReadAsync(channel, nodeIds, result);
                        result.ReusedSession = reused;
                        return;
                    }
                    catch when (attempt == 0)
                    {
                        Forget(endpointUrl, channel);
                        result.Items.Clear();
                        result.Ok = false;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<(ClientSessionChannel channel, bool reused)> AcquireAsync(string endpointUrl, bool forceNew)
        {
            ClientSessionChannel? existing;
            lock (Gate) { Channels.TryGetValue(endpointUrl, out existing); }
            if (!forceNew && existing != null && existing.State == CommunicationState.Opened)
                return (existing, true);

            if (existing != null) Forget(endpointUrl, existing);

            var fresh = new ClientSessionChannel(
                AppDescription,
                null,
                new AnonymousIdentity(),
                endpointUrl,
                SecurityPolicyUris.None);
            await fresh.OpenAsync();
            lock (Gate) { Channels[endpointUrl] = fresh; }
            return (fresh, false);
        }

        private static void Forget(string endpointUrl, ClientSessionChannel ch)
        {
            lock (Gate)
            {
                if (Channels.TryGetValue(endpointUrl, out var cur) && ReferenceEquals(cur, ch))
                    Channels.Remove(endpointUrl);
            }
            try { ch.AbortAsync().Wait(1000); } catch { }
        }

        private static async Task DoReadAsync(ClientSessionChannel channel, List<string> nodeIds, OpcUaReadResult result)
        {
            var readRequest = new ReadRequest
            {
                NodesToRead = nodeIds.Select(id => new ReadValueId
                {
                    NodeId = NodeId.Parse(id),
                    AttributeId = AttributeIds.Value
                }).ToArray()
            };

            var response = await channel.ReadAsync(readRequest);
            var results = response.Results ?? Array.Empty<DataValue>();
            result.Items.Clear();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                var item = new OpcUaReadItem { NodeId = nodeIds[i] };
                if (i < results.Length && results[i] != null)
                {
                    var dv = results[i];
                    item.StatusCode = dv.StatusCode.ToString();
                    if (StatusCode.IsGood(dv.StatusCode)) item.Value = dv.Value;
                    else item.Error = "Bad status: " + dv.StatusCode;
                }
                else item.Error = "no result";
                result.Items.Add(item);
            }
            result.Ok = true;
        }
    }
}
