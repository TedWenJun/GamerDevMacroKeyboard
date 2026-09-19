## Changes / 变更内容

<!-- What changed and why / 做了什么、为什么 -->

## Verification / 验证

- [ ] `dotnet build MacroHub.slnx -c Release` without warnings / 无警告
- [ ] `dotnet test MacroHub.slnx -c Release` passes / 通过
- [ ] Hook / HID / action execution / app protocol touched: `node tests/e2e/e2e.mjs` passes locally (summary attached) / 涉及钩子、HID、动作执行、应用协议：本机运行端到端测试通过（附结果摘要）
- [ ] Physical input touched: verified on a real pad (model and connection) / 涉及实物输入：在真实宏键盘上验证（说明型号与连接方式）
- [ ] Unreal plugin touched: builds on UE 5.8 and the panel works / 涉及 UE 插件：UE 5.8 编译通过、面板可用

## Documentation and languages / 文档与双语

- [ ] User-visible changes: `help.en.md` and `help.zh-CN.md` updated (including their changelog) / 用户可见改动已更新两份帮助（含更新记录）
- [ ] `CHANGELOG.md` and `CHANGELOG.zh-CN.md` Unreleased updated / 两份 CHANGELOG 的 Unreleased 已记录
- [ ] New UI text has English and Chinese (web `i18n.js`, plugin zh-Hans `.po`, tray) / 新界面文字中英齐全
- [ ] Protocol changes: `docs/protocol.md` and `docs/protocol.zh-CN.md` updated / 协议变更已更新两份协议文档
