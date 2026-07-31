using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine.Drivers
{
    public class ReactViteWebDriver : IOutputTargetDriver
    {
        public string TargetName => "web";
        public string SupportedExtension => ".html";

        public Task<Dictionary<string, byte[]>> GenerateOutputFilesAsync(
            OutputDriverContext context, CancellationToken cancellationToken = default)
        {
            var files = new Dictionary<string, byte[]>();

            var indexHtml = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>{context.ApplicationName}</title>
</head>
<body>
    <div id=""root""></div>
    <script type=""module"" src=""/src/main.tsx""></script>
</body>
</html>";

            var appName = context.ApplicationName.ToLowerInvariant().Replace(" ", "-");
            var packageJson = $$"""
{
  "name": "{{appName}}",
  "private": true,
  "version": "1.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "lucide-react": "^0.344.0"
  },
  "devDependencies": {
    "@types/react": "^18.2.66",
    "@types/react-dom": "^18.2.22",
    "@vitejs/plugin-react": "^4.2.1",
    "typescript": "^5.2.2",
    "vite": "^5.1.6"
  }
}
""";

            var mainTsx = @"import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import './index.css'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
)";

            files["index.html"] = Encoding.UTF8.GetBytes(indexHtml);
            files["package.json"] = Encoding.UTF8.GetBytes(packageJson);
            files["src/main.tsx"] = Encoding.UTF8.GetBytes(mainTsx);

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
