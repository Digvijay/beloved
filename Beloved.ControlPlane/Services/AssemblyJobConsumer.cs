using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Beloved.AssemblyEngine;
using Beloved.ControlPlane.Data;
using Beloved.ControlPlane.Hubs;
using Microsoft.EntityFrameworkCore;

namespace Beloved.ControlPlane.Services;

public class AssemblyJobConsumer : IConsumer<AssemblyJobMessage>
{
    private readonly AssemblyCompiler _compiler;
    private readonly IOutputStore _outputStore;
    private readonly IHubContext<AssemblyHub> _hubContext;
    private readonly BelovedDbContext _db;
    private readonly ILogger<AssemblyJobConsumer> _logger;

    public AssemblyJobConsumer(
        AssemblyCompiler compiler,
        IOutputStore outputStore,
        IHubContext<AssemblyHub> hubContext,
        BelovedDbContext db,
        ILogger<AssemblyJobConsumer> logger)
    {
        _compiler = compiler;
        _outputStore = outputStore;
        _hubContext = hubContext;
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AssemblyJobMessage> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Processing assembly job: {JobId}", msg.QueueJobId);

        // Update DB state
        var jobEntity = await _db.AssemblyJobs.FirstOrDefaultAsync(j => j.QueueJobId == msg.QueueJobId);
        if (jobEntity != null)
        {
            jobEntity.Status = "Processing";
            await _db.SaveChangesAsync();
        }

        try
        {
            var result = await _compiler.AssembleAsync(
                JsonSerializer.Serialize(msg.Blueprint),
                msg.QueueJobId,
                _outputStore,
                async logMsg =>
                {
                    _logger.LogInformation("[Job Log] {Log}", logMsg);
                    await _hubContext.Clients.Group(msg.QueueJobId).SendAsync("ReceiveLog", logMsg);
                });

            if (jobEntity != null)
            {
                jobEntity.Status = result.Success ? "Completed" : "Failed";
                jobEntity.SbomJson = result.SbomJson;
                await _db.SaveChangesAsync();
            }

            await _hubContext.Clients.Group(msg.QueueJobId).SendAsync("AssemblyCompleted", result.Success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assembly failed for job {JobId}", msg.QueueJobId);
            if (jobEntity != null)
            {
                jobEntity.Status = "Failed";
                await _db.SaveChangesAsync();
            }
            await _hubContext.Clients.Group(msg.QueueJobId).SendAsync("ReceiveLog", $"[Error] {ex.Message}");
            await _hubContext.Clients.Group(msg.QueueJobId).SendAsync("AssemblyCompleted", false);
        }
    }
}
