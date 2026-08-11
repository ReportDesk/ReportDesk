(() => {
    const badge = document.getElementById("notificationUnreadBadge");
    const list = document.getElementById("notificationList");
    const dropdown = document.getElementById("notificationDropdown");
    const enablePushButton = document.getElementById("enablePushNotifications");
    if (!badge || !list || !dropdown) return;

    let unreadCount = 0;
    let knownNotificationIds = new Set();

    const priorityClass = priority => priority === "Critical" ? "danger" : priority === "High" ? "warning" : "neutral";
    const priorityBadgeClass = priority => priority === "Critical" ? "text-bg-danger" : priority === "High" ? "text-bg-warning" : "bg-white border text-dark";
    const setUnreadCount = count => {
        unreadCount = count;
        badge.textContent = String(count);
        badge.classList.toggle("d-none", count === 0);
    };

    const relativeTime = value => new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));

    function notificationItem(notification) {
        const item = document.createElement("div");
        item.className = `p-3 border-bottom ${notification.isRead ? "bg-white" : "bg-light"}`;
        const title = document.createElement("div");
        title.className = "fw-semibold small";
        title.textContent = notification.title;
        const message = document.createElement("div");
        message.className = "small text-muted mt-1";
        message.textContent = notification.message;
        const metadata = document.createElement("div");
        metadata.className = "d-flex align-items-center gap-2 mt-2";
        metadata.innerHTML = `<span class="badge ${priorityBadgeClass(notification.priority)}">${notification.priority}</span><span class="small text-muted">${relativeTime(notification.createdDate)}</span>`;
        const actions = document.createElement("div");
        actions.className = "d-flex gap-2 mt-2";
        const open = document.createElement("a");
        open.className = "btn btn-sm btn-outline-primary";
        open.href = notification.reportUrl;
        open.textContent = "Open report";
        open.addEventListener("click", () => markRead(notification.notificationId));
        actions.appendChild(open);
        item.append(title, message, metadata, actions);
        return item;
    }

    async function markRead(notificationId) {
        await fetch(`/api/notifications/${notificationId}/read`, { method: "POST" });
    }

    async function loadNotifications(announceNew = false) {
        const response = await fetch("/api/notifications");
        if (!response.ok) return;
        const data = await response.json();
        const notifications = data.notifications || [];
        const newNotifications = announceNew
            ? notifications.filter(notification => !knownNotificationIds.has(notification.notificationId))
            : [];
        knownNotificationIds = new Set(notifications.map(notification => notification.notificationId));
        setUnreadCount(data.unreadCount || 0);
        list.replaceChildren();
        if (!notifications.length) {
            const empty = document.createElement("div");
            empty.className = "p-3 text-muted small";
            empty.textContent = "No notifications yet.";
            list.appendChild(empty);
            return;
        }
        notifications.forEach(notification => list.appendChild(notificationItem(notification)));
        newNotifications.forEach(notification => {
            showToast(notification);
            document.dispatchEvent(new CustomEvent("reportdesk:notification", { detail: notification }));
        });
        if (newNotifications.length && window.location.pathname.toLowerCase() === "/admin/reportlist") window.location.reload();
    }

    const toBase64 = bytes => btoa(String.fromCharCode(...new Uint8Array(bytes)));
    const base64UrlToBytes = value => {
        const padded = `${value}${"=".repeat((4 - value.length % 4) % 4)}`.replace(/-/g, "+").replace(/_/g, "/");
        return Uint8Array.from(atob(padded), character => character.charCodeAt(0));
    };

    async function enablePushNotifications() {
        if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) {
            enablePushButton.textContent = "Device alerts are not supported by this browser";
            enablePushButton.disabled = true;
            return;
        }
        const keyResponse = await fetch("/api/notifications/push-public-key");
        if (!keyResponse.ok) {
            enablePushButton.textContent = "Device alerts are not configured";
            enablePushButton.disabled = true;
            return;
        }
        if (Notification.permission === "denied") {
            enablePushButton.textContent = "Device alerts blocked in browser settings";
            enablePushButton.disabled = true;
            return;
        }
        const permission = await Notification.requestPermission();
        if (permission !== "granted") return;

        const { publicKey } = await keyResponse.json();
        const registration = await navigator.serviceWorker.register("/service-worker.js");
        const subscription = await registration.pushManager.subscribe({
            userVisibleOnly: true,
            applicationServerKey: base64UrlToBytes(publicKey)
        });
        const response = await fetch("/api/notifications/push-subscriptions", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                endpoint: subscription.endpoint,
                p256dh: toBase64(subscription.getKey("p256dh")),
                auth: toBase64(subscription.getKey("auth"))
            })
        });
        if (!response.ok) throw new Error("Could not save the device subscription.");
        enablePushButton.textContent = "Device alerts enabled";
        enablePushButton.disabled = true;
    }

    function showToast(notification) {
        let container = document.querySelector(".notification-alert-stack");
        if (!container) {
            container = document.createElement("div");
            container.className = "notification-alert-stack";
            document.body.appendChild(container);
        }
        const bellBounds = dropdown.getBoundingClientRect();
        const panelWidth = Math.min(360, window.innerWidth - 32);
        container.style.width = `${panelWidth}px`;
        container.style.top = `${bellBounds.bottom + 8}px`;
        container.style.left = `${Math.max(16, Math.min(window.innerWidth - panelWidth - 16, bellBounds.right - panelWidth))}px`;

        const alert = document.createElement("article");
        alert.className = `notification-alert notification-alert--${priorityClass(notification.priority)}`;
        alert.setAttribute("role", "alert");
        const icon = document.createElement("i");
        icon.className = notification.priority === "Critical" ? "bi bi-exclamation-octagon-fill" : "bi bi-bell-fill";
        const content = document.createElement("div");
        content.className = "notification-alert__content";
        const title = document.createElement("div");
        title.className = "notification-alert__title";
        title.textContent = notification.title;
        const message = document.createElement("div");
        message.className = "notification-alert__message";
        message.textContent = notification.message;
        const actions = document.createElement("div");
        actions.className = "notification-alert__actions";
        const open = document.createElement("a");
        open.href = notification.reportUrl;
        open.className = "notification-alert__open";
        open.textContent = "View report";
        const close = document.createElement("button");
        close.type = "button";
        close.className = "notification-alert__close";
        close.setAttribute("aria-label", "Dismiss notification");
        close.innerHTML = "&times;";
        const dismiss = () => {
            alert.classList.remove("is-visible");
            window.setTimeout(() => alert.remove(), 180);
        };
        close.addEventListener("click", dismiss);
        actions.append(open, close);
        content.append(title, message, actions);
        alert.append(icon, content);
        container.appendChild(alert);
        window.requestAnimationFrame(() => alert.classList.add("is-visible"));
        window.setTimeout(dismiss, 10000);
    }

    dropdown.addEventListener("show.bs.dropdown", loadNotifications);
    if (window.signalR) {
        const connection = new signalR.HubConnectionBuilder().withUrl("/hubs/notifications").withAutomaticReconnect().build();
        connection.on("NotificationReceived", notification => {
            knownNotificationIds.add(notification.notificationId);
            setUnreadCount(unreadCount + 1);
            showToast(notification);
            document.dispatchEvent(new CustomEvent("reportdesk:notification", { detail: notification }));
            if (window.location.pathname.toLowerCase() === "/admin/reportlist") window.location.reload();
        });
        connection.start().catch(() => { /* The notification centre still works on refresh. */ });
    }
    loadNotifications();
    window.setInterval(() => loadNotifications(true), 15000);
    window.addEventListener("resize", () => document.querySelectorAll(".notification-alert-stack").forEach(stack => {
        const bellBounds = dropdown.getBoundingClientRect();
        const panelWidth = Math.min(360, window.innerWidth - 32);
        stack.style.width = `${panelWidth}px`;
        stack.style.top = `${bellBounds.bottom + 8}px`;
        stack.style.left = `${Math.max(16, Math.min(window.innerWidth - panelWidth - 16, bellBounds.right - panelWidth))}px`;
    }));
    enablePushButton?.addEventListener("click", () => enablePushNotifications().catch(() => {
        enablePushButton.textContent = "Could not enable device alerts";
    }));
})();
