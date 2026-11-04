/* ES5 syntax: keep the launcher usable in CEP 9. */
(function () {
  "use strict";
  var button = document.getElementById("launch");
  var status = document.getElementById("status");
  var host = document.getElementById("host");
  var language = document.getElementById("language");
  var cep = window.__adobe_cep__;
  var environment = null;
  var environmentError = false;
  var selected = /^zh/i.test(navigator.language || navigator.userLanguage || "") ? "zh-CN" : "en";
  var messages = {
    "zh-CN": {
      description: "在指针旁召回面板；每槽独立选择实时镜像或移动真实浮动面板。",
      compatibility: "兼容目标：PS 2019 及以上。目前尚未实机验证。",
      shortcuts: "默认 Alt + 1～5 召唤／放回，可自定义或分设放回键。",
      ready: "解压完整安装包后，打开工具绑定面板。也可直接运行 helper/PanelMirror.exe。",
      launch: "打开面板工具",
      noHost: "无法识别 Photoshop。请在 Photoshop 的 CEP 面板中使用此启动器。",
      version: "当前 Photoshop：{0}；属于兼容目标，尚未验证。",
      unsupported: "当前主机版本 {0} 低于兼容目标或无法识别。",
      noInterface: "CEP 进程接口不可用；请直接运行 helper/PanelMirror.exe。",
      noPath: "无法读取扩展的 Windows 绝对路径。",
      missing: "未找到原生助手。请重新解压完整 ZIP，保留 helper 文件夹。",
      failed: "助手启动请求失败，CEP 错误：{0}",
      sent: "已发送启动／唤回请求。若未显示，请从 Windows 系统托盘打开配置。",
      unknown: "未知"
    },
    en: {
      description: "Recall panels near your pointer. Choose a live mirror or a real floating panel for each slot.",
      compatibility: "Target: Photoshop 2019 and later. Host runtime testing is pending.",
      shortcuts: "Alt + 1–5 summons/restores. Customize shortcuts or use separate return keys.",
      ready: "Extract the complete ZIP, then open the tool to bind panels. You can also run helper/PanelMirror.exe directly.",
      launch: "Open Panel Tools",
      noHost: "Photoshop was not identified. Use this launcher inside a Photoshop CEP panel.",
      version: "Photoshop {0}: within the target range; testing pending.",
      unsupported: "Host version {0} is below the target range or could not be identified.",
      noInterface: "CEP process interface unavailable. Run helper/PanelMirror.exe directly.",
      noPath: "Cannot read an absolute Windows extension path.",
      missing: "Native helper missing. Extract the complete ZIP again, keeping its helper folder.",
      failed: "Helper launch request failed; CEP error: {0}",
      sent: "Launch/recall requested. If configuration is not visible, open it from the Windows tray.",
      unknown: "unknown"
    }
  };
  try {
    var saved = window.localStorage.getItem("panel-tools.language");
    if (saved === "en" || saved === "zh-CN") selected = saved;
  } catch (ignored) { /* Storage is optional in older hosts. */ }
  function text(key, value) { return messages[selected][key].replace("{0}", value || ""); }
  function render() {
    document.documentElement.lang = selected;
    language.value = selected;
    document.getElementById("description").textContent = text("description");
    document.getElementById("compatibility").textContent = text("compatibility");
    document.getElementById("shortcuts").textContent = text("shortcuts");
    button.textContent = text("launch");
    status.textContent = text("ready");
    var version = environment ? String(environment.appVersion || "") : "";
    host.textContent = environmentError ? text("noHost") :
      text(parseInt(version, 10) >= 20 ? "version" : "unsupported", version || text("unknown"));
  }
  try {
    if (!cep || typeof cep.getHostEnvironment !== "function") throw new Error("noHost");
    environment = cep.getHostEnvironment();
    if (typeof environment === "string") environment = JSON.parse(environment);
    if (!environment || (environment.appId !== "PHXS" && environment.appId !== "PHSP")) throw new Error("noHost");
  } catch (ignored) { environmentError = true; }
  language.addEventListener("change", function () {
    selected = language.value === "zh-CN" ? "zh-CN" : "en";
    try { window.localStorage.setItem("panel-tools.language", selected); } catch (ignored) { }
    render();
  });
  function extensionPath(value) {
    var path = String(value || "");
    if (/^file:\/\//i.test(path)) {
      if (/^file:\/\/\//i.test(path)) path = path.replace(/^file:\/\/\//i, "");
      else path = "//" + path.replace(/^file:\/\//i, "");
      path = decodeURIComponent(path);
    }
    path = path.replace(/^\/(?=[A-Za-z]:)/, "").replace(/[\\\/]+$/, "").replace(/\//g, "\\");
    if (!/^(?:[A-Za-z]:\\|\\\\)/.test(path)) throw new Error(text("noPath"));
    return path;
  }
  button.addEventListener("click", function () {
    try {
      if (environmentError || !environment || !(parseInt(environment.appVersion, 10) >= 20) ||
          !cep || typeof cep.getSystemPath !== "function" || !cep.process || typeof cep.process.createProcess !== "function")
        throw new Error(text("noInterface"));
      var executable = extensionPath(cep.getSystemPath("extension")) + "\\helper\\PanelMirror.exe";
      if (cep.fs && typeof cep.fs.stat === "function") {
        var exists = cep.fs.stat(executable);
        if (!exists || exists.err !== 0) throw new Error(text("missing"));
      }
      var result = cep.process.createProcess(executable);
      if (!result || result.err !== 0) throw new Error(text("failed", result ? String(result.err) : text("unknown")));
      status.textContent = text("sent");
    } catch (error) { status.textContent = error.message || String(error); }
  });
  render();
}());
