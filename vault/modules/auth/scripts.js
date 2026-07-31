// Auth Module Runtime Bindings
document.addEventListener('DOMContentLoaded', () => {
    const loginForm = document.getElementById('auth-login-form');
    const registerForm = document.getElementById('auth-register-form');

    if (loginForm) {
        loginForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = document.getElementById('login-username').value;
            const password = document.getElementById('login-password').value;
            const errDiv = document.getElementById('login-error');
            errDiv.style.display = 'none';

            try {
                const res = await app.request('/auth/login', {
                    method: 'POST',
                    body: JSON.stringify({ username, password })
                });
                app.setAuth(res.token, res.user);
                // Redirect to first page in navigation that isn't auth
                const homeLink = Array.from(document.querySelectorAll('.nav-links a'))
                    .find(a => !a.getAttribute('href').includes('login') && !a.getAttribute('href').includes('register'));
                if (homeLink) {
                    app.navigate(homeLink.getAttribute('href').substring(1));
                } else {
                    app.navigate('login');
                }
            } catch (err) {
                errDiv.textContent = err.message || 'Login failed';
                errDiv.style.display = 'block';
            }
        });
    }

    if (registerForm) {
        registerForm.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = document.getElementById('register-username').value;
            const password = document.getElementById('register-password').value;
            const errDiv = document.getElementById('register-error');
            errDiv.style.display = 'none';

            try {
                const res = await app.request('/auth/register', {
                    method: 'POST',
                    body: JSON.stringify({ username, password })
                });
                app.setAuth(res.token, res.user);
                const homeLink = Array.from(document.querySelectorAll('.nav-links a'))
                    .find(a => !a.getAttribute('href').includes('login') && !a.getAttribute('href').includes('register'));
                if (homeLink) {
                    app.navigate(homeLink.getAttribute('href').substring(1));
                } else {
                    app.navigate('login');
                }
            } catch (err) {
                errDiv.textContent = err.message || 'Registration failed';
                errDiv.style.display = 'block';
            }
        });
    }
});
