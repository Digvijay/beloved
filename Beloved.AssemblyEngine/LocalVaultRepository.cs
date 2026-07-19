using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine
{
    public class LocalVaultRepository : IVaultRepository
    {
        private readonly string _vaultPath;

        public LocalVaultRepository(string workspaceRoot)
        {
            _vaultPath = Path.Combine(workspaceRoot, "vault");
        }

        public Task<(string targetDirectory, string digest)> FetchTemplateAsync(string templateName, string targetDirectory)
        {
            var sourcePath = Path.Combine(_vaultPath, "templates", templateName);
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Template {templateName} not found in vault.");
            }

            CopyDirectory(sourcePath, targetDirectory);
            return Task.FromResult((targetDirectory, "local-digest-" + templateName));
        }

        public Task<(string targetDirectory, string digest)> FetchModuleAsync(string moduleName, string version, string targetDirectory)
        {
            var sourcePath = Path.Combine(_vaultPath, "modules", moduleName.ToLower());
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Module {moduleName} not found in vault.");
            }

            CopyDirectory(sourcePath, targetDirectory);
            return Task.FromResult((targetDirectory, "local-digest-" + moduleName));
        }

        public Task<(Dictionary<string, byte[]> files, string digest)> FetchTemplateInMemoryAsync(string templateName)
        {
            var sourcePath = Path.Combine(_vaultPath, "templates", templateName);
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Template {templateName} not found in vault.");
            }

            var files = LoadDirectoryToMemory(sourcePath);
            return Task.FromResult((files, "local-digest-" + templateName));
        }

        public Task<(Dictionary<string, byte[]> files, string digest)> FetchModuleInMemoryAsync(string moduleName, string version)
        {
            var sourcePath = Path.Combine(_vaultPath, "modules", moduleName.ToLower());
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"Module {moduleName} not found in vault.");
            }

            var files = LoadDirectoryToMemory(sourcePath);
            return Task.FromResult((files, "local-digest-" + moduleName));
        }

        private static Dictionary<string, byte[]> LoadDirectoryToMemory(string sourceDir)
        {
            var files = new Dictionary<string, byte[]>();
            LoadDirectoryToMemoryInternal(sourceDir, sourceDir, files);
            return files;
        }

        private static void LoadDirectoryToMemoryInternal(string rootDir, string currentDir, Dictionary<string, byte[]> files)
        {
            foreach (var file in Directory.GetFiles(currentDir))
            {
                var relPath = Path.GetRelativePath(rootDir, file);
                files[relPath] = File.ReadAllBytes(file);
            }

            foreach (var directory in Directory.GetDirectories(currentDir))
            {
                LoadDirectoryToMemoryInternal(rootDir, directory, files);
            }
        }

        public Task PushModuleAsync(string modulePath, string moduleName, string version)
        {
            var moduleDest = Path.Combine(_vaultPath, "modules", moduleName.ToLower());
            Directory.CreateDirectory(moduleDest);
            CopyDirectory(modulePath, moduleDest);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<string>> ListModulesAsync()
        {
            var modulesPath = Path.Combine(_vaultPath, "modules");
            if (!Directory.Exists(modulesPath))
            {
                return Task.FromResult(Enumerable.Empty<string>());
            }

            var modules = Directory.GetDirectories(modulesPath)
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .Cast<string>();

            return Task.FromResult(modules);
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                string targetDirPath = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirPath);
            }
        }

        public Task<bool> VerifySignatureAsync(string moduleName, string version)
        {
            var sourcePath = Path.Combine(_vaultPath, "modules", moduleName.ToLower());
            return Task.FromResult(Directory.Exists(sourcePath));
        }
    }
}
