# Beloved CLI Tool (`beloved`)

Command-line tool for **Beloved** — an AI-assisted application assembly platform.  
Rather than generating code line-by-line, Beloved assembles production-ready Web, API, and Desktop applications in seconds by stitching pre-audited, cryptographically signed OCI component modules.

## Installation

Install as a global .NET tool:

```bash
dotnet tool install --global Beloved.Cli
```

## Quick Start

### 1. Generate an Application

Assemble a full-stack SaaS application with auth, billing, and analytics:

```bash
beloved generate "Build a SaaS with auth, billing, and analytics"
```

### 2. Push a Custom OCI Module

Package and push a custom module layer to your OCI-compliant registry:

```bash
beloved module push my-module http://localhost:5001
```

### 3. Check Assembly Status

```bash
beloved status --job-id <JOB_ID>
```

## License

[MIT](https://github.com/Digvijay/beloved/blob/main/LICENSE) © 2026 Digvijay Chauhan & Beloved Contributors
