using OpenTelemetry;
using OpenTelemetry.Exporter;

namespace DevDash.Extensions.OpenTelemetry;

public static class Bootstrapping
{
    extension(IOpenTelemetryBuilder builder)
    {
        public IOpenTelemetryBuilder UseDevDashOtlpExporter(int port = 5284)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            
            return builder.UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri($"http://localhost:{port}"));
        }
    }
}