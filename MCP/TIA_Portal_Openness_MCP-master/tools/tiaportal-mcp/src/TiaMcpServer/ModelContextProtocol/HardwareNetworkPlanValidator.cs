using System;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// 硬件/网络计划的离线校验器。
    /// 目的：在真正写入 TIA 项目前，先拦住猜测路径、非法 IP 和不明确的 CPU 属性写入。
    /// </summary>
    public static class HardwareNetworkPlanValidator
    {
        public static JsonObject Validate(string planJson)
        {
            var errors = new JsonArray();
            var warnings = new JsonArray();
            JsonObject? root = null;

            try
            {
                root = JsonNode.Parse(planJson) as JsonObject;
            }
            catch (Exception ex)
            {
                errors.Add("Invalid JSON: " + ex.Message);
            }

            var operations = root?["operations"] as JsonArray;
            if (root != null && operations == null)
                errors.Add("Missing operations array.");

            if (operations != null)
            {
                for (var i = 0; i < operations.Count; i++)
                {
                    if (operations[i] is not JsonObject op)
                    {
                        errors.Add($"operations[{i}] must be an object.");
                        continue;
                    }

                    var type = op["type"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        errors.Add($"operations[{i}].type is required.");
                        continue;
                    }

                    switch (type)
                    {
                        case "EnsureSubnet":
                            ValidateEnsureSubnet(op, i, errors, warnings);
                            break;
                        case "AttachDeviceNodeToSubnet":
                            ValidateAttach(op, i, errors, warnings);
                            break;
                        case "SetCpuCommonSettings":
                            ValidateCpuSettings(op, i, errors, warnings);
                            break;
                        default:
                            errors.Add($"operations[{i}].type '{type}' is not supported. Use EnsureSubnet, AttachDeviceNodeToSubnet, or SetCpuCommonSettings.");
                            break;
                    }
                }
            }

            return new JsonObject
            {
                ["ok"] = errors.Count == 0,
                ["offlineOnly"] = true,
                ["connectsToTia"] = false,
                ["modifiesProject"] = false,
                ["requiresReadbackAfterApply"] = true,
                ["operationCount"] = operations?.Count ?? 0,
                ["errors"] = errors,
                ["warnings"] = warnings
            };
        }

        private static void ValidateEnsureSubnet(JsonObject op, int index, JsonArray errors, JsonArray warnings)
        {
            RequireResolvedPath(op, "anchorDeviceItemPath", index, errors);
            RequireNonEmpty(op, "subnetName", index, errors);

            var subnetType = op["subnetType"]?.ToString() ?? string.Empty;
            if (!IsSupportedSubnetType(subnetType))
                errors.Add($"operations[{index}].subnetType must be IndustrialEthernet/PROFINET/PN/IE.");

            ValidateOptionalNetworkFields(op, index, errors, warnings);
        }

        private static void ValidateAttach(JsonObject op, int index, JsonArray errors, JsonArray warnings)
        {
            RequireResolvedPath(op, "deviceItemPath", index, errors);
            RequireNonEmpty(op, "subnetName", index, errors);

            var node = op["interfaceIndex"];
            if (node == null || !int.TryParse(node.ToString(), out var interfaceIndex) || interfaceIndex < 0)
                errors.Add($"operations[{index}].interfaceIndex must be a zero-based non-negative integer from network-node discovery readback.");

            if (op.ContainsKey("anchorDeviceItemPath"))
                RequireResolvedPath(op, "anchorDeviceItemPath", index, errors);

            ValidateOptionalNetworkFields(op, index, errors, warnings);
        }

        private static void ValidateCpuSettings(JsonObject op, int index, JsonArray errors, JsonArray warnings)
        {
            RequireResolvedPath(op, "cpuPath", index, errors);
            var settings = op["settings"] as JsonObject;
            var exact = settings?["exactAttributes"] as JsonObject;
            if (exact == null || exact.Count == 0)
                errors.Add($"operations[{index}].settings.exactAttributes is required. Attribute names must come from GetDeviceItemInfo/GetDeviceItemNetworkInfo readback.");

            if (settings != null)
            {
                foreach (var key in settings.Select(kv => kv.Key))
                {
                    if (!string.Equals(key, "exactAttributes", StringComparison.Ordinal))
                        errors.Add($"operations[{index}].settings.{key} is not accepted. Use exactAttributes only.");
                }
            }

            if (exact != null)
            {
                foreach (var kv in exact)
                {
                    if (LooksLikeAlias(kv.Key))
                        errors.Add($"operations[{index}].settings.exactAttributes.{kv.Key} looks like an alias. Use the exact TIA attribute name from readback.");
                    if (string.IsNullOrWhiteSpace(kv.Value?.ToString()))
                        warnings.Add($"operations[{index}].settings.exactAttributes.{kv.Key} has an empty value.");
                }
            }
        }

        private static void ValidateOptionalNetworkFields(JsonObject op, int index, JsonArray errors, JsonArray warnings)
        {
            ValidateIp(op, "ip", index, errors);
            ValidateIp(op, "gateway", index, errors);

            var mask = op["mask"]?.ToString();
            if (!string.IsNullOrWhiteSpace(mask) && !IsValidSubnetMask(mask!))
                errors.Add($"operations[{index}].mask is not a valid IPv4 subnet mask.");

            if (!string.IsNullOrWhiteSpace(op["gateway"]?.ToString()) && string.IsNullOrWhiteSpace(op["ip"]?.ToString()))
                warnings.Add($"operations[{index}].gateway was supplied without ip; verify the target attribute model before applying.");
        }

        private static void ValidateIp(JsonObject op, string propertyName, int index, JsonArray errors)
        {
            var value = op[propertyName]?.ToString();
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                errors.Add($"operations[{index}].{propertyName} is not a valid IPv4 address.");
        }

        private static void RequireNonEmpty(JsonObject op, string propertyName, int index, JsonArray errors)
        {
            if (string.IsNullOrWhiteSpace(op[propertyName]?.ToString()))
                errors.Add($"operations[{index}].{propertyName} is required.");
        }

        private static void RequireResolvedPath(JsonObject op, string propertyName, int index, JsonArray errors)
        {
            var value = op[propertyName]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"operations[{index}].{propertyName} is required.");
                return;
            }

            if (LooksGuessed(value))
                errors.Add($"operations[{index}].{propertyName} looks guessed. Resolve it from GetProjectTree/GetDeviceItemTree first.");
        }

        private static bool LooksGuessed(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Contains("?", StringComparison.Ordinal) || trimmed.Contains("*", StringComparison.Ordinal))
                return true;
            if (trimmed.StartsWith("TODO", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("example", StringComparison.OrdinalIgnoreCase))
                return true;
            return trimmed.Equals("PLC", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("CPU", StringComparison.OrdinalIgnoreCase)
                   || trimmed.Equals("HMI", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeAlias(string value)
        {
            var lower = value.Trim().ToLowerInvariant();
            return lower is "ip" or "ipaddress" or "ipv4" or "mask" or "subnetmask" or "gateway" or "profinetname" or "devicename";
        }

        private static bool IsSupportedSubnetType(string subnetType)
        {
            var value = (subnetType ?? string.Empty).Trim();
            return value.Equals("PROFINET", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("PN", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("IndustrialEthernet", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("Industrial Ethernet", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("PN/IE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidSubnetMask(string value)
        {
            if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            var bytes = ip.GetAddressBytes();
            var mask = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            var inverted = ~mask;
            return (inverted & (inverted + 1)) == 0;
        }
    }
}
