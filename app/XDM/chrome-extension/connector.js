"use strict";
import Logger from './logger.js';

const APP_BASE_URL = "http://127.0.0.1:8597";

export default class Connector {
    constructor(onMessage, onDisconnect) {
        this.logger = new Logger();
        this.onMessage = onMessage;
        this.onDisconnect = onDisconnect;
        this.connected = undefined;
    }

    connect() {
        for (let i = 0; i < 12; i++) {
            chrome.alarms.create("alerm-" + i, {
                periodInMinutes: 1,
                when: Date.now() + 1000 + ((i + 1) * 5000)
            });
        }
        chrome.alarms.onAlarm.addListener(this.onTimer.bind(this));
    }

    onTimer() {
        this.sync();
    }

    /**
     * Asks XDM for its current configuration. Resolves once the answer has been applied
     * (or the connection marked as lost), so callers can decide with fresh state.
     */
    sync(timeoutMs = 2000) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), timeoutMs);
        return fetch(APP_BASE_URL + "/sync", { signal: controller.signal })
            .then(this.onResponse.bind(this))
            .catch(err => this.disconnect())
            .finally(() => clearTimeout(timer));
    }

    disconnect() {
        this.connected = false;
        this.onDisconnect();
    }

    isConnected() {
        return this.connected;
    }

    onResponse(res) {
        this.connected = true;
        return res.json().then(json => this.onMessage(json)).catch(err => this.disconnect());
    }

    postMessage(url, data) {
        fetch(APP_BASE_URL + url, { method: "POST", body: JSON.stringify(data) })
            .then(this.onResponse.bind(this))
            .catch(err => this.disconnect());
    }

    launchApp() {

    }
}