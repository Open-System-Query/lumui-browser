namespace Lumui.Browser.Shell;

public static class BrowserShellDocument
{
    public const String Source = """
    {
      "lumui_surface": "1.0",
      "app_id": "lumui.browser",
      "surface_id": "browser.shell",
      "revision": 1,
      "mode": "primary",
      "title": "Lumi",
      "requested_page_id": "browser",
      "identity": {
        "name": "Lumi",
        "short_name": "Lumi",
        "home": "https://lumuiopensource.com/",
        "icon": {
          "source": "https://lumuiopensource.com/LUMUI_icon.png",
          "alt": "Lumi"
        },
        "brand": {
          "accent": "#008F80",
          "accent_secondary": "#F4777D",
          "accent_tertiary": "#67C4DC",
          "highlight": "#FFD166",
          "ink": "#111111",
          "cool": "#E8F7F4",
          "motif": "none"
        }
      },
      "pages": [
        {
          "id": "browser",
          "title": "Lumi",
          "role": "application",
          "regions": [
            {
              "id": "browser.chrome",
              "kind": "section",
              "role": "supporting",
              "items": [
                { "id": "browser.newTab", "kind": "button", "label": "New tab", "help": "Open a new tab", "action": "browser.newTab" },
                { "id": "browser.back", "kind": "button", "label": "Back", "help": "Return to the previous page", "action": "browser.back" },
                { "id": "browser.forward", "kind": "button", "label": "Forward", "help": "Go to the next page", "action": "browser.forward" },
                { "id": "browser.reload", "kind": "button", "label": "Reload", "help": "Reload this page", "action": "browser.reload" },
                { "id": "browser.home", "kind": "button", "label": "Home", "help": "Open the home page", "action": "browser.home" },
                { "id": "browser.open", "kind": "button", "label": "Open", "help": "Open the address", "action": "browser.open" },
                { "id": "browser.bookmark", "kind": "button", "label": "Bookmark", "help": "Save this page", "action": "browser.bookmark" },
                { "id": "browser.reading", "kind": "button", "label": "Reading", "help": "Adjust reading and display", "action": "browser.reading" },
                { "id": "browser.tools", "kind": "button", "label": "Developer tools", "help": "Inspect this LUMUI page", "action": "browser.tools" },
                { "id": "browser.menu", "kind": "button", "label": "Menu", "help": "Open the browser menu", "action": "browser.menu" }
              ]
            },
            {
              "id": "browser.menu.commands",
              "kind": "section",
              "role": "supporting",
              "items": [
                { "id": "menu.newTab", "kind": "button", "label": "New tab", "action": "browser.newTab" },
                { "id": "menu.newWindow", "kind": "button", "label": "New window", "action": "browser.newWindow" },
                { "id": "menu.newPrivateWindow", "kind": "button", "label": "New private window", "action": "browser.newPrivateWindow" },
                { "id": "menu.fullScreen", "kind": "button", "label": "Full screen", "action": "browser.fullScreen" },
                { "id": "menu.bookmarks", "kind": "button", "label": "Bookmarks", "action": "browser.bookmarks" },
                { "id": "menu.history", "kind": "button", "label": "History", "action": "browser.history" },
                { "id": "menu.downloads", "kind": "button", "label": "Downloads", "action": "browser.downloads" },
                { "id": "menu.passwords", "kind": "button", "label": "Passwords", "action": "browser.passwords" },
                { "id": "menu.settings", "kind": "button", "label": "Settings", "action": "browser.settings" },
                { "id": "menu.tools", "kind": "button", "label": "Developer tools", "action": "browser.tools" }
              ]
            },
            {
              "id": "browser.utility.surfaces",
              "kind": "section",
              "role": "supporting",
              "items": [
                { "id": "utility.reading", "kind": "text", "text": "Reading" },
                { "id": "utility.settings", "kind": "text", "text": "Settings" },
                { "id": "utility.bookmarks", "kind": "text", "text": "Bookmarks" },
                { "id": "utility.history", "kind": "text", "text": "History" },
                { "id": "utility.downloads", "kind": "text", "text": "Downloads" },
                { "id": "utility.passwords", "kind": "text", "text": "Passwords" },
                { "id": "utility.tools", "kind": "text", "text": "Developer tools" }
              ]
            }
          ]
        },
        {
          "id": "reading",
          "title": "Reading",
          "role": "settings",
          "regions": [
            {
              "id": "reading.controls",
              "kind": "section",
              "role": "supporting",
              "items": [
                { "id": "reading.textSize", "kind": "slider", "label": "Text size", "help": "Resize text and reflow the page", "value": 100, "min": 90, "max": 180, "step": 10, "unit": "percent", "action": "browser.changeSetting" },
                { "id": "reading.theme", "kind": "comboBox", "label": "Theme", "help": "Choose a light or dark page", "value": "light", "options": [{ "label": "Light", "value": "light" }, { "label": "Dark", "value": "dark" }] },
                { "id": "reading.font", "kind": "comboBox", "label": "Font", "help": "Use any installed font", "value": "default", "options": [{ "label": "Default", "value": "default" }] },
                { "id": "reading.bionic", "kind": "toggle", "label": "Bionic reading", "help": "Emphasize the start of words", "value": false, "action": "browser.changeSetting" },
                { "id": "reading.readingView", "kind": "toggle", "label": "Reading view", "help": "Use a clear, linear reading order", "value": false, "action": "browser.changeSetting" },
                { "id": "reading.highContrast", "kind": "toggle", "label": "High contrast", "help": "Strengthen text and control boundaries", "value": false, "action": "browser.changeSetting" },
                { "id": "reading.reducedMotion", "kind": "toggle", "label": "Reduce motion", "help": "Avoid animation and decorative movement", "value": false, "action": "browser.changeSetting" },
                { "id": "reading.guided", "kind": "toggle", "label": "Guided mode", "help": "Use larger controls and simpler choices", "value": false, "action": "browser.changeSetting" },
                { "id": "reading.colorVision", "kind": "comboBox", "label": "Color vision", "help": "Keep important colors easy to distinguish", "value": "default", "options": [{ "label": "Default", "value": "default" }, { "label": "Red and green", "value": "red-green" }, { "label": "Green and red", "value": "green-red" }, { "label": "Blue and yellow", "value": "blue-yellow" }] },
                { "id": "reading.more", "kind": "button", "label": "More settings", "action": "browser.settings" },
                { "id": "reading.restore", "kind": "button", "label": "Restore defaults", "confirmation": "implicit", "action": "browser.changeSetting" }
              ]
            }
          ]
        },
        {
          "id": "settings",
          "title": "Settings",
          "role": "settings",
          "regions": [
            {
              "id": "settings.general",
              "kind": "section",
              "label": "General",
              "items": [
                { "id": "settings.startup", "kind": "comboBox", "label": "Startup", "value": "home", "options": [{ "label": "Home page", "value": "home" }, { "label": "Previous tabs", "value": "previous" }] },
                { "id": "settings.homePage", "kind": "textField", "label": "Home page", "value": "https://lumuiopensource.com/", "action": "browser.changeSetting" },
                { "id": "settings.newTabs", "kind": "comboBox", "label": "New tabs", "value": "home", "options": [{ "label": "Home page", "value": "home" }, { "label": "Blank page", "value": "blank" }, { "label": "Custom page", "value": "custom" }] },
                { "id": "settings.newTabPage", "kind": "textField", "label": "New tab page", "value": "https://lumuiopensource.com/", "action": "browser.changeSetting" },
                { "id": "settings.confirmTabs", "kind": "toggle", "label": "Close multiple tabs", "value": true, "action": "browser.changeSetting" },
                { "id": "settings.passwords", "kind": "button", "label": "Passwords", "action": "browser.passwords" }
              ]
            },
            {
              "id": "settings.privacy",
              "kind": "section",
              "label": "Privacy",
              "items": [
                { "id": "settings.history", "kind": "toggle", "label": "Browsing history", "value": true, "action": "browser.changeSetting" },
                { "id": "settings.doNotTrack", "kind": "toggle", "label": "Do Not Track", "value": true, "action": "browser.changeSetting" },
                { "id": "settings.clearOnExit", "kind": "toggle", "label": "Clear on exit", "value": false, "action": "browser.changeSetting" },
                { "id": "settings.clearData", "kind": "button", "label": "Clear data", "confirmation": "dangerous", "action": "browser.clearData" },
                { "id": "settings.savePasswords", "kind": "toggle", "label": "Save passwords", "value": true, "action": "browser.changeSetting" },
                { "id": "settings.fillPasswords", "kind": "toggle", "label": "Fill passwords", "value": true, "action": "browser.changeSetting" }
              ]
            },
            {
              "id": "settings.downloads",
              "kind": "section",
              "label": "Downloads",
              "items": [
                { "id": "settings.downloadFolder", "kind": "textField", "label": "Save files to", "value": "Downloads", "action": "browser.changeSetting" },
                { "id": "settings.askDownload", "kind": "toggle", "label": "Choose each location", "value": true, "action": "browser.changeSetting" }
              ]
            },
            {
              "id": "settings.permissions",
              "kind": "section",
              "label": "Permissions",
              "items": [
                { "id": "settings.sensitiveAccess", "kind": "toggle", "label": "Sensitive access", "value": true, "action": "browser.changeSetting" }
              ]
            }
          ]
        },
        {
          "id": "bookmarks",
          "title": "Bookmarks",
          "role": "application",
          "regions": [{ "id": "bookmarks.content", "kind": "section", "items": [{ "id": "bookmarks.search", "kind": "searchField", "label": "Search bookmarks", "value": "", "action": "browser.search" }, { "id": "bookmarks.list", "kind": "list", "items": [] }] }]
        },
        {
          "id": "history",
          "title": "History",
          "role": "application",
          "regions": [{ "id": "history.content", "kind": "section", "items": [{ "id": "history.search", "kind": "searchField", "label": "Search history", "value": "", "action": "browser.search" }, { "id": "history.list", "kind": "list", "items": [] }] }]
        },
        {
          "id": "downloads",
          "title": "Downloads",
          "role": "application",
          "regions": [{ "id": "downloads.content", "kind": "section", "items": [{ "id": "downloads.search", "kind": "searchField", "label": "Search files", "value": "", "action": "browser.search" }, { "id": "downloads.openFolder", "kind": "button", "label": "Open downloads folder", "action": "browser.openUtility" }, { "id": "downloads.list", "kind": "list", "items": [] }] }]
        },
        {
          "id": "passwords",
          "title": "Passwords",
          "role": "application",
          "regions": [{ "id": "passwords.content", "kind": "section", "items": [{ "id": "passwords.search", "kind": "searchField", "label": "Search passwords", "value": "", "action": "browser.search" }, { "id": "passwords.add", "kind": "button", "label": "Add", "action": "browser.openUtility" }, { "id": "passwords.list", "kind": "list", "items": [] }] }]
        },
        {
          "id": "developerTools",
          "title": "Developer tools",
          "role": "application",
          "regions": [
            {
              "id": "developerTools.content",
              "kind": "section",
              "items": [
                {
                  "id": "tools.tabs",
                  "kind": "tabs",
                  "label": "Developer tools",
                  "selected": "tools.overview",
                  "tabs": [
                    { "id": "tools.overview", "kind": "text", "text": "Overview" },
                    { "id": "tools.source", "kind": "text", "text": "Source" },
                    { "id": "tools.structure", "kind": "text", "text": "Structure" },
                    { "id": "tools.network", "kind": "text", "text": "Network" },
                    { "id": "tools.problems", "kind": "text", "text": "Problems" },
                    { "id": "tools.accessibility", "kind": "text", "text": "Accessibility" },
                    { "id": "tools.actions", "kind": "text", "text": "Actions" },
                    { "id": "tools.diagnostics", "kind": "text", "text": "Diagnostics" }
                  ]
                }
              ]
            }
          ]
        }
      ],
      "actions": {
        "browser.newTab": { "callback": "browser.newTab", "confirmation": "none", "idempotent": true },
        "browser.back": { "callback": "browser.back", "confirmation": "none", "idempotent": true },
        "browser.forward": { "callback": "browser.forward", "confirmation": "none", "idempotent": true },
        "browser.reload": { "callback": "browser.reload", "confirmation": "none", "idempotent": true },
        "browser.home": { "callback": "browser.home", "confirmation": "none", "idempotent": true },
        "browser.open": { "callback": "browser.open", "confirmation": "none" },
        "browser.bookmark": { "callback": "browser.bookmark", "confirmation": "none" },
        "browser.reading": { "callback": "browser.reading", "confirmation": "none", "idempotent": true },
        "browser.tools": { "callback": "browser.tools", "confirmation": "none", "idempotent": true },
        "browser.menu": { "callback": "browser.menu", "confirmation": "none", "idempotent": true },
        "browser.newWindow": { "callback": "browser.newWindow", "confirmation": "none" },
        "browser.newPrivateWindow": { "callback": "browser.newPrivateWindow", "confirmation": "none" },
        "browser.fullScreen": { "callback": "browser.fullScreen", "confirmation": "none", "idempotent": true },
        "browser.bookmarks": { "callback": "browser.bookmarks", "confirmation": "none", "idempotent": true },
        "browser.history": { "callback": "browser.history", "confirmation": "none", "idempotent": true },
        "browser.downloads": { "callback": "browser.downloads", "confirmation": "none", "idempotent": true },
        "browser.passwords": { "callback": "browser.passwords", "confirmation": "none", "idempotent": true },
        "browser.settings": { "callback": "browser.settings", "confirmation": "none", "idempotent": true },
        "browser.clearData": { "callback": "browser.clearData", "confirmation": "dangerous" },
        "browser.changeSetting": { "callback": "browser.changeSetting", "confirmation": "none" },
        "browser.search": { "callback": "browser.search", "confirmation": "none", "idempotent": true },
        "browser.openUtility": { "callback": "browser.openUtility", "confirmation": "none", "idempotent": true }
      },
      "state": {
        "renderer": "avalonia-native",
        "profile": "desktop"
      }
    }
    """;
}
