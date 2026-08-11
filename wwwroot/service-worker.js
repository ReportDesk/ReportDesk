self.addEventListener("push", event => {
    const payload = event.data ? event.data.json() : {};
    const title = payload.title || "Report Desk notification";
    const options = {
        body: payload.message || "You have a new report notification.",
        icon: "/favicon.ico",
        badge: "/favicon.ico",
        data: { reportUrl: payload.reportUrl || "/" },
        tag: `reportdesk-${payload.reportUrl || "notification"}`,
        renotify: payload.priority === "Critical"
    };
    event.waitUntil(self.registration.showNotification(title, options));
});

self.addEventListener("notificationclick", event => {
    event.notification.close();
    event.waitUntil(clients.openWindow(event.notification.data.reportUrl));
});
