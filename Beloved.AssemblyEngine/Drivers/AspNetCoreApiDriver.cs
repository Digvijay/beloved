using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine.Drivers
{
    public class AspNetCoreApiDriver : IOutputTargetDriver
    {
        public string TargetName => "api";
        public string SupportedExtension => ".csproj";

        public Task<Dictionary<string, byte[]>> GenerateOutputFilesAsync(
            OutputDriverContext context, CancellationToken cancellationToken = default)
        {
            var files = new Dictionary<string, byte[]>();

            var csproj = @"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>";

            var programCs = $@"var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapGet(""/health"", () => Results.Ok(new {{ status = ""Healthy"", app = ""{context.ApplicationName}"" }}));
app.MapGet(""/"", () => Results.Ok(""{context.ApplicationName} Backend API - Powered by Beloved""));

app.Run();";

            var appsettingsJson = @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""AllowedHosts"": ""*""
}";

            var projectName = context.ApplicationName.Replace(" ", "");
            files[$"{projectName}.csproj"] = Encoding.UTF8.GetBytes(csproj);
            files["Program.cs"] = Encoding.UTF8.GetBytes(programCs);
            files["appsettings.json"] = Encoding.UTF8.GetBytes(appsettingsJson);

            if (context.SharedAssets != null)
            {
                foreach (var (path, bytes) in context.SharedAssets)
                {
                    files[path] = bytes;
                }
            }

            return Task.FromResult(files);
        }
    }
}
