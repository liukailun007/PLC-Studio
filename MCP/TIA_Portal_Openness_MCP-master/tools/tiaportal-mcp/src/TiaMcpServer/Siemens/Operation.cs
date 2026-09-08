using Microsoft.Extensions.Logging;
using System;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// Centralizes repetitive try/catch boilerplate in Portal.cs methods.
    /// One call replaces ~8 lines of identical exception-handling code per method.
    /// </summary>
    internal static class Operation
    {
        /// <summary>
        /// Runs <paramref name="body"/> and returns true on success.
        /// Logs PortalException at Warning, all others at Error.
        /// Returns false on any exception (never rethrows).
        /// </summary>
        public static bool Run(ILogger? logger, string operationName, Action body)
        {
            try
            {
                body();
                return true;
            }
            catch (PortalException pex)
            {
                logger?.LogWarning(pex, "{Op} failed: [{Code}] {Msg}", operationName, pex.Code, pex.Message);
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "{Op} unexpected failure: {Msg}", operationName, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> and returns its result, or null on any exception.
        /// </summary>
        public static T? Run<T>(ILogger? logger, string operationName, Func<T> body)
            where T : class
        {
            try
            {
                return body();
            }
            catch (PortalException pex)
            {
                logger?.LogWarning(pex, "{Op} failed: [{Code}] {Msg}", operationName, pex.Code, pex.Message);
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "{Op} unexpected failure: {Msg}", operationName, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> and returns its result, or <paramref name="fallback"/> on any exception.
        /// </summary>
        public static T RunValue<T>(ILogger? logger, string operationName, Func<T> body, T fallback = default!)
            where T : struct
        {
            try
            {
                return body();
            }
            catch (PortalException pex)
            {
                logger?.LogWarning(pex, "{Op} failed: [{Code}] {Msg}", operationName, pex.Code, pex.Message);
                return fallback;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "{Op} unexpected failure: {Msg}", operationName, ex.Message);
                return fallback;
            }
        }
    }
}
