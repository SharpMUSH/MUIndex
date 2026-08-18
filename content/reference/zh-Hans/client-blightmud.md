---
kind: client
slug: blightmud
title: Blightmud
summary: 一个用 Rust 写的现代终端客户端，有 Lua 脚本、内置的文本转语音，以及一个会向服务器自报的屏幕阅读器模式。
home: https://github.com/Blightmud/Blightmud
platform: Linux
platform: macOS
platform: Windows (WSL only)
capability: screen reader | yes | https://github.com/Blightmud/Blightmud
capability: TLS | yes | https://github.com/Blightmud/Blightmud
capability: UTF-8 | yes | https://github.com/Blightmud/Blightmud
capability: MCCP | yes | https://github.com/Blightmud/Blightmud
capability: GMCP | yes | https://github.com/Blightmud/Blightmud
capability: MSDP | yes | https://github.com/Blightmud/Blightmud
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | yes | https://github.com/Blightmud/Blightmud
see-also: clients/tintin
see-also: clients/mudlet
see-also: protocols/ttype
---

Blightmud 是一个用 Rust 写的终端客户端，GPL 3，也是本节中发布最活跃的客户端之一。脚本是 Lua。它只
在终端里跑：没有原生的 Windows 构建，Windows 用户是在 WSL 下运行它。

## 无障碍

Blightmud 在这方面有三块彼此不同的东西，比一行所能承载的要多：

- 一个**对屏幕阅读器友好的模式**（`--reader-mode`，或 `reader_mode` 设置），它把终端界面改成阅读器
  跟得上的样子。它不支持状态区。
- **内置的文本转语音**，作为一个可选的编译项，并带有脚本可以调用的 Lua API——其中包括一个
  `tts.gag()`，用来让匹配到的行不被读出来。文档很坦白地说明，把它的 TTS 和屏幕阅读器一起用，未必总
  是一个愉快的组合。
- **自动的 MTTS 通告**：在阅读器模式下，或者启用了 TTS 时，它会把 `MTTS_SCREEN_READER` 加进它向服
  务器自述的内容里，这样在意这一点的游戏就可以做出调整。

和 TinTin++ 一样，这里没有点名任何具体的屏幕阅读器，所以这是一个有文档的模式，而不是与某个产品经过
测试的兼容性。

## 表里写着未知的地方

**MXP**、**MSP** 和 **ATCP** 在项目的 README 和随附的帮助里都不曾出现。**MCCP** 有文档说明是 v2；
v1 是否也一并处理，我们没有确立。
