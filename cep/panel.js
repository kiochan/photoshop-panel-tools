/* ES5 syntax: keep the launcher usable in older CEP browser runtimes. */
(function () {
  "use strict";
  var button = document.getElementById("launch");
  var status = document.getElementById("status");
  var host = document.getElementById("host");
  var cep = window.__adobe_cep__;
  var targets = { "20": "2019", "21": "2020", "26": "2025" };

  try {
    if (!cep || typeof cep.getHostEnvironment !== "function") throw new Error("请在 Photoshop CEP 面板内打开。");
    var environment = cep.getHostEnvironment();
    if (typeof environment === "string") environment = JSON.parse(environment);
    var version = String(environment.appVersion || "");
    var major = String(parseInt(version, 10));
    host.textContent = targets[major] ? "当前：PS " + targets[major] + "（" + version + "），属于兼容目标，尚未验证。" :
      "当前主机版本：" + (version || "未识别") + (parseInt(major, 10) >= 20 ? "；属于 2019 及以上兼容目标，尚未验证。" : "；低于最低兼容目标或版本无法识别。");
  } catch (error) {
    host.textContent = "无法识别主机版本：" + (error.message || String(error));
  }

  function extensionPath(value) {
    var path = String(value || "");
    if (/^file:\/\//i.test(path)) {
      if (/^file:\/\/\//i.test(path)) path = path.replace(/^file:\/\/\//i, "");
      else path = "//" + path.replace(/^file:\/\//i, "");
      path = decodeURIComponent(path);
    }
    path = path.replace(/^\/(?=[A-Za-z]:)/, "").replace(/[\\\/]+$/, "").replace(/\//g, "\\");
    if (!/^(?:[A-Za-z]:\\|\\\\)/.test(path)) throw new Error("未获得 Windows 扩展绝对路径。");
    return path;
  }

  button.addEventListener("click", function () {
    try {
      if (!cep || typeof cep.getSystemPath !== "function" || !cep.process || typeof cep.process.createProcess !== "function") {
        throw new Error("需要 Photoshop 2019 及以上可用的 CEP 进程接口，也可以单独打开原生助手。");
      }
      var executable = extensionPath(cep.getSystemPath("extension")) + "\\helper\\PanelMirror.exe";
      if (cep.fs && typeof cep.fs.stat === "function") {
        var exists = cep.fs.stat(executable);
        if (!exists || exists.err !== 0) throw new Error("未找到原生助手，请先执行项目中的 build.ps1。");
      }
      var result = cep.process.createProcess(executable);
      if (!result || result.err !== 0) throw new Error("助手启动请求失败，CEP 错误：" + (result ? result.err : "未知"));
      status.textContent = "启动请求已发送。若配置程序已运行，请切换到它的窗口。";
    } catch (error) {
      status.textContent = error.message || String(error);
    }
  });
}());
