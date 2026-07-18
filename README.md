# Relay AI API Studio

一个体积很小的 Windows 原生 AI API 调试客户端。

它使用 WinForms 原生桌面控件、`HttpClient` 和 `JavaScriptSerializer`，不包含浏览器、WebView、Chromium、Node.js 或本地网页服务器。

## 功能

- OpenAI 兼容的 Chat Completions 和 Responses API
- OpenAI、Xelv AI、OpenRouter、DeepSeek、通义千问、硅基流动、Moonshot、xAI、Groq、Mistral、Together AI、Perplexity、Ollama、LM Studio 等预设
- 从服务商的 `/models` 接口同步当前模型
- 流式响应、请求体和原始响应查看
- Temperature、Max Tokens、Reasoning、System Prompt、自定义 Headers
- 16 个编程、产品、分析、写作、学习和创意提示词模板
- API Key 只保存在进程内存，不写入配置文件

## 构建

在 Windows 上双击 `build.cmd`，或在 PowerShell 中运行：

```powershell
cmd /c build.cmd
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器，输出到 `dist/RelayAIStudio.exe`。目标机器需要 .NET Framework 4.x，这是 Windows 桌面系统常见的系统组件。

## 运行

直接运行 `dist/RelayAIStudio.exe`。选择服务商后填写 API Key，点击“同步模型”，再发送请求。

图像模型会在列表中标记为图像模型，但当前版本的对话发送路径只接受文本、推理和代码模型。

## 安全说明

API Key 不会被写入源码、配置、日志或仓库。不要把真实密钥提交到 Git；如果密钥曾经出现在公开聊天、Issue 或提交记录中，请立即在服务商后台轮换。

## License

MIT License，见 [LICENSE](LICENSE)。
