# 知言 Zhiyan

Windows 上的中文聊天辅助工具，为 QQ 和微信聊天生成可编辑的回复建议。
当前版本：4.3.0。

## 功能
- 工作台选择聊天窗口，进入跟随侧栏
- 本地文本/视觉采集，按【我】与【对方】区分消息
- Jev 判断回复策略，DeepSeek 生成中文候选回复
- 补充背景、刷新候选、复制回复
- 默认打开工作台；自动寻找聊天和登录 Windows 时启动均为可选项

## 使用
解压 Windows 运行包，运行 JevChatAssistant.exe。
在模型设置中分别填写 Jev 与回复服务的 API 配置，选择 QQ 或微信的实际聊天窗口。
生成的消息需要自行确认并发送。
API 调用可能产生服务商费用；变化后的聊天文字会发送给配置的模型服务。
密钥由 Windows DPAPI 按当前用户加密保存，不随源码或发行包分发。

## 从源码构建
需要 Windows、.NET Framework 4.8，以及 Windows PowerShell 或 PowerShell。

    .\build.ps1
    .\JevChatAssistant.exe

## 测试

    Start-Process .\JevChatAssistant.exe -ArgumentList '--self-test' -Wait
    Get-Content .\self-test-result.txt

当前 175 项自动断言通过。自动测试使用模拟 API，不消耗模型额度。
tests/chinese-cases.json 是 10 组原创中文回归场景。历史真实模型测试曾发现虚构迟回原因、扩写事实范围等问题，不能把少量样例当作通用准确率。
本项目调整应用提示词与回复处理逻辑，没有修改模型权重。

## 当前限制
- QQ / 微信版本和窗口布局可能影响识别；图片、表情、文件占位不等于已理解其内容。
- 中文回复仍需用户核对，尤其是行程、承诺和缺失的个人事实。
- 4.3.0 完成长文本侧栏渲染检查；最新的实时交互复验尚未全部完成。
- 迟回理由检查只覆盖部分常见表达，不是通用事实验证。

源码不包含个人配置、聊天日志、截屏、密钥或已安装版本的历史文件。