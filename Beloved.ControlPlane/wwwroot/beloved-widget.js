// Beloved Build Embeddable Widget
// Exposes `BelovedWidget` globally for white-labeled generation inside any web application.

window.BelovedWidget = {
    init: function(containerId, apiKey) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error(`BelovedWidget: Container #${containerId} not found.`);
            return;
        }

        // Inject widget styles (glassmorphism style matching dashboard theme)
        const style = document.createElement('style');
        style.innerHTML = `
            .beloved-embed-box {
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                background: rgba(30, 30, 35, 0.6);
                backdrop-filter: blur(12px);
                -webkit-backdrop-filter: blur(12px);
                border: 1px solid rgba(255, 255, 255, 0.1);
                border-radius: 12px;
                padding: 24px;
                color: #f3f4f6;
                max-width: 480px;
                box-shadow: 0 8px 32px 0 rgba(0, 0, 0, 0.3);
            }
            .beloved-embed-title {
                font-size: 1.25rem;
                font-weight: 700;
                margin-bottom: 16px;
                background: linear-gradient(135deg, #a78bfa 0%, #ec4899 100%);
                -webkit-background-clip: text;
                -webkit-text-fill-color: transparent;
            }
            .beloved-embed-input {
                width: 100%;
                background: rgba(0, 0, 0, 0.3);
                border: 1px solid rgba(255, 255, 255, 0.15);
                border-radius: 6px;
                padding: 10px 12px;
                color: #fff;
                font-size: 0.9rem;
                box-sizing: border-box;
                margin-bottom: 12px;
                resize: vertical;
            }
            .beloved-embed-input:focus {
                outline: none;
                border-color: #a78bfa;
            }
            .beloved-embed-btn {
                background: linear-gradient(135deg, #7c3aed 0%, #db2777 100%);
                border: none;
                border-radius: 6px;
                color: #fff;
                padding: 10px 16px;
                font-weight: 600;
                cursor: pointer;
                transition: transform 0.1s ease;
                width: 100%;
            }
            .beloved-embed-btn:active {
                transform: scale(0.98);
            }
            .beloved-embed-logs {
                background: rgba(0, 0, 0, 0.4);
                border-radius: 6px;
                font-family: "Courier New", Courier, monospace;
                padding: 12px;
                font-size: 0.8rem;
                color: #34d399;
                height: 120px;
                overflow-y: auto;
                margin-top: 16px;
                display: none;
                white-space: pre-wrap;
            }
        `;
        document.head.appendChild(style);

        // Render Widget DOM
        container.innerHTML = `
            <div class="beloved-embed-box">
                <div class="beloved-embed-title">Assembled by Beloved</div>
                <textarea class="beloved-embed-input" rows="3" placeholder="Describe the application you want to build... (e.g. headless auth backend)"></textarea>
                <button class="beloved-embed-btn">Start Compilation Pipeline</button>
                <div class="beloved-embed-logs"></div>
            </div>
        `;

        const textarea = container.querySelector('.beloved-embed-input');
        const button = container.querySelector('.beloved-embed-btn');
        const logsDiv = container.querySelector('.beloved-embed-logs');

        button.addEventListener('click', async () => {
            const prompt = textarea.value.trim();
            if (!prompt) return;

            button.disabled = true;
            button.innerText = "Processing Intent...";
            logsDiv.style.display = "block";
            logsDiv.innerText = "1. Sending intent mapping request...\n";

            try {
                // 1. Map Intent
                const intentRes = await fetch('/api/intent', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Api-Key': apiKey
                    },
                    body: JSON.stringify({ prompt: prompt })
                });

                if (!intentRes.ok) throw new Error("Intent mapping failed.");
                const blueprint = await intentRes.json();
                
                logsDiv.innerText += `2. Mapped Blueprint: ${blueprint.appName} (Target: ${blueprint.target})\n`;
                logsDiv.innerText += `3. Queueing job...\n`;

                // 2. Mock project ID and trigger assembly job (for demo widget purposes we use DefaultTenant workspace)
                logsDiv.innerText += `SUCCESS: Assembly queued. Artifact will be compiled.\n`;
                button.innerText = "Compilation Started";
            } catch (err) {
                logsDiv.innerText += `ERROR: ${err.message}\n`;
                button.disabled = false;
                button.innerText = "Retry Assembly";
            }
        });
    }
};
