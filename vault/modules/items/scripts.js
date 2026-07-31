// Items Module Client Operations
window.itemsModule = {
    async loadItems() {
        if (!app.token) {
            document.getElementById('items-table-body').innerHTML = `
                <tr>
                    <td colspan="5" style="text-align: center; opacity: 0.5; padding: 2rem;">
                        Authentication required. Please log in first.
                    </td>
                </tr>
            `;
            return;
        }

        try {
            const items = await app.request('/items');
            this.renderItems(items);
        } catch (err) {
            console.error('Failed to load items:', err);
        }
    },

    renderItems(items) {
        const tbody = document.getElementById('items-table-body');
        if (!tbody) return;

        if (items.length === 0) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="5" style="text-align: center; opacity: 0.5; padding: 2rem;">
                        No items found. Create one to get started!
                    </td>
                </tr>
            `;
            return;
        }

        tbody.innerHTML = items.map(item => `
            <tr id="item-row-${item.id}">
                <td style="font-weight: 600;">${this.escapeHtml(item.name)}</td>
                <td style="opacity: 0.8; font-size: 0.9rem;">${this.escapeHtml(item.description || '-')}</td>
                <td><span class="badge badge-success">${item.quantity}</span></td>
                <td style="opacity: 0.6; font-size: 0.85rem;">${new Date(item.created_at).toLocaleDateString()}</td>
                <td>
                    <button class="btn btn-secondary" style="padding: 0.3rem 0.6rem; font-size: 0.8rem; border-color: rgba(214, 48, 49, 0.3); color: var(--error);" onclick="window.itemsModule.deleteItem(${item.id})">
                        <i class="fa-solid fa-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    },

    async deleteItem(id) {
        if (!confirm('Are you sure you want to delete this item?')) return;
        try {
            await app.request(`/items/${id}`, { method: 'DELETE' });
            this.loadItems();
        } catch (err) {
            alert(err.message || 'Delete failed');
        }
    },

    escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
    }
};

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('items-create-form');
    if (form) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('item-name').value;
            const description = document.getElementById('item-desc').value;
            const quantity = parseInt(document.getElementById('item-qty').value);

            try {
                await app.request('/items', {
                    method: 'POST',
                    body: JSON.stringify({ name, description, quantity })
                });
                form.reset();
                window.itemsModule.loadItems();
            } catch (err) {
                alert(err.message || 'Failed to create item');
            }
        });
    }

    // Auto load when navigating to view
    app.on('view:items', () => {
        window.itemsModule.loadItems();
    });
});
