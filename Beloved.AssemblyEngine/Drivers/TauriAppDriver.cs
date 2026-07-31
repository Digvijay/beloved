using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine.Drivers
{
    public class TauriAppDriver : IOutputTargetDriver
    {
        public string TargetName => "desktop";
        public string SupportedExtension => ".app";

        public async Task<Dictionary<string, byte[]>> GenerateOutputFilesAsync(
            OutputDriverContext context, CancellationToken cancellationToken = default)
        {
            var webDriver = new ReactViteWebDriver();
            var files = await webDriver.GenerateOutputFilesAsync(context, cancellationToken);

            var tauriConfigJson = JsonSerializer.Serialize(new
            {
                build = new
                {
                    beforeDevCommand = "npm run dev",
                    beforeBuildCommand = "npm run build",
                    devPath = "http://localhost:1420",
                    distDir = "../dist"
                },
                package = new
                {
                    productName = context.ApplicationName,
                    version = "0.1.0"
                },
                tauri = new
                {
                    allowlist = new { all = true },
                    windows = new[]
                    {
                        new
                        {
                            title = context.ApplicationName,
                            width = 1280,
                            height = 800,
                            resizable = true,
                            fullscreen = false
                        }
                    },
                    security = new { csp = (string?)null }
                }
            }, new JsonSerializerOptions { WriteIndented = true });

            var cargoToml = $@"[package]
name = ""{context.ApplicationName.ToLowerInvariant().Replace(" ", "-")}""
version = ""0.1.0""
description = ""{context.Description}""
authors = [""Beloved Core Engine""]
edition = ""2021""

[build-dependencies]
tauri-build = {{ version = ""1.5"", features = [] }}

[dependencies]
tauri = {{ version = ""1.5"", features = [""shell-open""] }}
serde = {{ version = ""1.0"", features = [""derive""] }}
serde_json = ""1.0""
";

            var mainRs = @"#![cfg_attr(not(debug_assertions), windows_subsystem = ""windows"")]

fn main() {
  tauri::Builder::default()
    .run(tauri::generate_context!())
    .expect(""error while running tauri application"");
}
";

            files["src-tauri/tauri.conf.json"] = Encoding.UTF8.GetBytes(tauriConfigJson);
            files["src-tauri/Cargo.toml"] = Encoding.UTF8.GetBytes(cargoToml);
            files["src-tauri/src/main.rs"] = Encoding.UTF8.GetBytes(mainRs);

            return files;
        }
    }
}
