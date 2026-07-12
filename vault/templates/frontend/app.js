// Beloved Core Client Runtime
class BelovedApp {
    constructor() {
        this.apiBase = window.location.origin.includes('localhost') || window.location.origin.includes('127.0.0.1')
            ? 'http://localhost:5005/api'
            : '/api';
        this.token = localStorage.getItem('beloved_auth_token');
        this.user = JSON.parse(localStorage.getItem('beloved_user') || 'null');
        this.currentView = '';
        this.listeners = {};
    }

    init() {
        this.setupNavigation();
        this.checkAuth();
        this.routeToDefault();
    }

    setupNavigation() {
        document.querySelectorAll('.nav-links a').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                const viewId = link.getAttribute('href').substring(1);
                this.navigate(viewId);
            });
        });
    }

    navigate(viewId) {
        // Toggle view visibility
        document.querySelectorAll('.view-section').forEach(view => {
            view.classList.remove('active');
        });
        
        const targetView = document.getElementById(`view-${viewId}`);
        if (targetView) {
            targetView.classList.add('active');
            this.currentView = viewId;
            
            // Update active state in nav
            document.querySelectorAll('.nav-links li').forEach(li => {
                li.classList.remove('active');
                if (li.querySelector(`a[href="#${viewId}"]`)) {
                    li.classList.add('active');
                }
            });

            // Update title
            const titleElem = document.getElementById('current-view-title');
            if (titleElem) {
                const navLink = document.querySelector(`.nav-links a[href="#${viewId}"]`);
                titleElem.textContent = navLink ? navLink.textContent.trim() : viewId;
            }

            // Fire view load hooks
            this.emit(`view:${viewId}`);
        }
    }

    routeToDefault() {
        const firstNav = document.querySelector('.nav-links a');
        if (firstNav) {
            const firstViewId = firstNav.getAttribute('href').substring(1);
            this.navigate(firstViewId);
        }
    }

    async request(path, options = {}) {
        const url = `${this.apiBase}${path}`;
        const headers = {
            'Content-Type': 'application/json',
            ...(this.token ? { 'Authorization': `Bearer ${this.token}` } : {}),
            ...options.headers
        };

        const config = {
            ...options,
            headers
        };

        try {
            const res = await fetch(url, config);
            if (res.status === 401) {
                this.logout();
                throw new Error('Session expired');
            }
            const data = await res.json();
            if (!res.ok) throw new Error(data.message || 'API request failed');
            return data;
        } catch (err) {
            console.error(`API Error on ${path}:`, err);
            throw err;
        }
    }

    setAuth(token, user) {
        this.token = token;
        this.user = user;
        localStorage.setItem('beloved_auth_token', token);
        localStorage.setItem('beloved_user', JSON.stringify(user));
        this.checkAuth();
        this.emit('auth:change', { token, user });
    }

    logout() {
        this.token = null;
        this.user = null;
        localStorage.removeItem('beloved_auth_token');
        localStorage.removeItem('beloved_user');
        this.checkAuth();
        this.emit('auth:change', null);
        this.navigate('login');
    }

    checkAuth() {
        const profileWidget = document.getElementById('user-profile-widget');
        if (profileWidget) {
            if (this.user) {
                profileWidget.innerHTML = `
                    <div style="display:flex; align-items:center; gap:0.75rem;">
                        <span style="font-size:0.9rem;">${this.user.username}</span>
                        <button class="btn btn-secondary" style="padding:0.4rem 0.8rem; font-size:0.8rem;" onclick="app.logout()">
                            <i class="fa-solid fa-right-from-bracket"></i>
                        </button>
                    </div>
                `;
            } else {
                profileWidget.innerHTML = `<span class="badge badge-error">Not logged in</span>`;
            }
        }
    }

    on(event, callback) {
        if (!this.listeners[event]) this.listeners[event] = [];
        this.listeners[event].push(callback);
    }

    emit(event, data) {
        if (this.listeners[event]) {
            this.listeners[event].forEach(cb => cb(data));
        }
    }
}

const app = new BelovedApp();
window.app = app;
document.addEventListener('DOMContentLoaded', () => app.init());
