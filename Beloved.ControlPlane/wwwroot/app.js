// Beloved Dashboard Controller
let availableModules = [];
let currentBlueprint = null;
let signalrConnection = null;
let currentJobId = null;
let activeProjectId = null;
const API_KEY = "beloved-dev-key";

document.addEventListener('DOMContentLoaded', async () => {
    await initializeProject();
    loadModules();
    setupEventListeners();
    setupSignalR();
    log('Beloved Control Plane initialized.', 'info');
});

async function initializeProject() {
    try {
        log('Initializing tenant workspace...', 'info');
        
        // 1. Fetch existing projects
        const response = await fetch('/api/projects', {
            headers: { 'X-Api-Key': API_KEY }
        });
        
        if (!response.ok) throw new Error('Failed to fetch projects');
        
        const projects = await response.json();
        
        if (projects.length > 0) {
            activeProjectId = projects[0].id;
            log(`Loaded active project: ${projects[0].name}`, 'success');
        } else {
            // 2. Create a new project if none exists
            log('No projects found. Provisioning new workspace...', 'info');
            const createResp = await fetch('/api/projects', {
                method: 'POST',
                headers: { 
                    'Content-Type': 'application/json',
                    'X-Api-Key': API_KEY
                },
                body: JSON.stringify({ name: 'Default Dashboard Project' })
            });
            
            if (!createResp.ok) throw new Error('Failed to create project');
            const newProject = await createResp.json();
            activeProjectId = newProject.id;
            log(`Provisioned new workspace: ${newProject.name}`, 'success');
        }
    } catch (err) {
        log('Failed to initialize workspace: ' + err.message, 'error');
    }
}

function setupSignalR() {
    signalrConnection = new signalR.HubConnectionBuilder()
        .withUrl("/assemblyhub")
        .withAutomaticReconnect()
        .build();

    signalrConnection.on("ReceiveLog", (message, type) => {
        log(message, type);
        
        // Show preview controls when assembly finishes successfully
        if (message.includes("Artifact is now available for download") && currentJobId) {
            document.getElementById('btn-assemble').innerHTML = `<i class="fa-solid fa-check"></i> Assembly Complete`;
            document.getElementById('preview-controls').style.display = 'flex';
        }
    });

    signalrConnection.start().then(() => {
        log("WebSocket connected to /assemblyhub", "success");
    }).catch(err => {
        log("Failed to connect WebSocket: " + err.toString(), "error");
    });
}

function setupEventListeners() {
    const btnMap = document.getElementById('btn-map-intent');
    if (btnMap) btnMap.addEventListener('click', mapIntent);

    const btnAssemble = document.getElementById('btn-assemble');
    if (btnAssemble) btnAssemble.addEventListener('click', assembleApplication);

    const btnPreviewStart = document.getElementById('btn-preview-start');
    if (btnPreviewStart) btnPreviewStart.addEventListener('click', startPreview);

    const btnPreviewStop = document.getElementById('btn-preview-stop');
    if (btnPreviewStop) btnPreviewStop.addEventListener('click', stopPreview);
}

// Load modules from Vault
async function loadModules() {
    try {
        log('Connecting to OCI Component Vault...', 'info');
        const response = await fetch('/api/modules');
        availableModules = await response.json();
        renderModules();
        log(`Successfully loaded ${availableModules.length} components from vault.`, 'success');
    } catch (err) {
        log('Error loading vault modules: ' + err.message, 'error');
    }
}

// Render modules in UI
function renderModules() {
    const container = document.getElementById('modules-container');
    if (!container) return;

    if (availableModules.length === 0) {
        container.innerHTML = `<div style="text-align: center; padding: 1rem; opacity: 0.5; grid-column: 1 / -1;">No modules available in the vault.</div>`;
        return;
    }

    container.innerHTML = availableModules.map(mod => {
        const isSelected = currentBlueprint?.modules?.includes(mod) || false;
        return `
        <div class="module-item ${isSelected ? 'selected' : ''}" id="mod-${mod.toLowerCase()}">
            <div class="module-info">
                <h4>${mod}</h4>
                <p>Component provided by OCI Vault</p>
            </div>
            <div class="module-checkbox">
                <i class="fa-solid fa-check"></i>
            </div>
        </div>
        `;
    }).join('');
}

// Map Intent via Ollama/OpenAI
async function mapIntent() {
    const input = document.getElementById('intent-input').value.trim();
    if (!input) {
        log('Please enter an application intent first.', 'error');
        return;
    }

    const btn = document.getElementById('btn-map-intent');
    btn.innerHTML = `<span class="loader"></span> Mapping...`;
    btn.disabled = true;

    log('Transmitting intent to deterministic mapper...', 'info');

    try {
        const response = await fetch('/api/intent', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ prompt: input })
        });

        if (!response.ok) {
            const errText = await response.text();
            throw new Error(errText);
        }

        currentBlueprint = await response.json();
        
        // Update UI
        renderModules();
        
        // Pretty print to code viewer
        const viewer = document.getElementById('blueprint-viewer');
        if (viewer) {
            viewer.innerHTML = syntaxHighlight(JSON.stringify(currentBlueprint, null, 2));
        }

        log(`Blueprint generated successfully for app: ${currentBlueprint.appName}`, 'success');
        
        // Enable assembly
        const assembleBtn = document.getElementById('btn-assemble');
        if (assembleBtn) assembleBtn.disabled = false;

    } catch (err) {
        log('Failed to map intent: ' + err.message, 'error');
    } finally {
        btn.innerHTML = `<i class="fa-solid fa-microchip"></i> Map Intent`;
        btn.disabled = false;
    }
}

// Assemble Application
async function assembleApplication() {
    if (!currentBlueprint) return;

    const btn = document.getElementById('btn-assemble');
    btn.innerHTML = `<span class="loader"></span> Assembling...`;
    btn.disabled = true;

    log(`Queuing assembly job for ${currentBlueprint.appName} in project ${activeProjectId}...`, 'info');

    try {
        const payload = {
            projectId: activeProjectId,
            blueprint: currentBlueprint
        };

        const response = await fetch('/api/assemble', {
            method: 'POST',
            headers: { 
                'Content-Type': 'application/json',
                'X-Api-Key': API_KEY
            },
            body: JSON.stringify(payload)
        });

        if (!response.ok) throw new Error('Failed to queue assembly job');

        const result = await response.json();
        currentJobId = result.jobId;
        
        log(`Job queued successfully. JobID: ${currentJobId}`, 'success');

        // Hide preview controls
        document.getElementById('preview-controls').style.display = 'none';
        document.getElementById('preview-link').style.display = 'none';

        // Join the SignalR group for this Job
        if (signalrConnection && signalrConnection.state === signalR.HubConnectionState.Connected) {
            await signalrConnection.invoke("JoinJobGroup", currentJobId);
        }

    } catch (err) {
        log('Assembly error: ' + err.message, 'error');
        btn.disabled = false;
        btn.innerHTML = `<i class="fa-solid fa-hammer"></i> Assemble Application`;
    }
}

async function startPreview() {
    if (!currentJobId) return;

    const btn = document.getElementById('btn-preview-start');
    btn.innerHTML = `<span class="loader"></span> Starting...`;
    btn.disabled = true;

    try {
        const response = await fetch('/api/preview/start', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ jobId: currentJobId })
        });

        if (!response.ok) {
            const err = await response.json();
            throw new Error(err.error || 'Failed to start preview');
        }

        const result = await response.json();
        log(`Sandbox live at: ${result.url}`, 'success');

        btn.style.display = 'none';
        
        const stopBtn = document.getElementById('btn-preview-stop');
        stopBtn.style.display = 'block';

        const linkBtn = document.getElementById('preview-link');
        linkBtn.href = result.url;
        linkBtn.style.display = 'block';

    } catch (err) {
        log('Preview error: ' + err.message, 'error');
        btn.innerHTML = `<i class="fa-solid fa-play"></i> Launch Sandbox Preview`;
        btn.disabled = false;
    }
}

async function stopPreview() {
    const btn = document.getElementById('btn-preview-stop');
    btn.innerHTML = `<span class="loader"></span> Stopping...`;
    btn.disabled = true;

    try {
        const response = await fetch('/api/preview/stop', { method: 'POST' });
        if (!response.ok) throw new Error('Failed to stop preview');

        log('Sandbox terminated.', 'info');

        btn.style.display = 'none';
        document.getElementById('preview-link').style.display = 'none';

        const startBtn = document.getElementById('btn-preview-start');
        startBtn.innerHTML = `<i class="fa-solid fa-play"></i> Launch Sandbox Preview`;
        startBtn.style.display = 'block';
        startBtn.disabled = false;
        
    } catch (err) {
        log('Stop preview error: ' + err.message, 'error');
        btn.innerHTML = `<i class="fa-solid fa-stop"></i> Stop`;
        btn.disabled = false;
    }
}

// Custom Logging
function log(message, type = 'info') {
    const consoleEl = document.getElementById('log-console');
    if (!consoleEl) return;

    const time = new Date().toLocaleTimeString([], { hour12: false });
    const line = document.createElement('div');
    line.className = 'log-line';
    line.innerHTML = `<span class="log-time">[${time}]</span> <span class="log-${type}">${message}</span>`;
    
    consoleEl.appendChild(line);
    consoleEl.scrollTop = consoleEl.scrollHeight;
}

// Simple JSON syntax highlighter
function syntaxHighlight(json) {
    json = json.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    return json.replace(/("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g, function (match) {
        let cls = 'number';
        if (/^"/.test(match)) {
            if (/:$/.test(match)) {
                cls = 'key';
                return `<span style="color: #c792ea;">${match.slice(0, -1)}</span>:`;
            } else {
                cls = 'string';
                return `<span style="color: #c3e88d;">${match}</span>`;
            }
        } else if (/true|false/.test(match)) {
            cls = 'boolean';
        } else if (/null/.test(match)) {
            cls = 'null';
        }
        return `<span style="color: #f78c6c;">${match}</span>`;
    });
}
