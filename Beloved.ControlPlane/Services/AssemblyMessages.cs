using System;
using Beloved.AssemblyEngine;

namespace Beloved.ControlPlane.Services;

public record AssemblyJobMessage(
    string QueueJobId,
    Blueprint Blueprint
);

public record IntentMappingMessage(
    Guid TenantId,
    string Prompt,
    string ConnectionId
);
