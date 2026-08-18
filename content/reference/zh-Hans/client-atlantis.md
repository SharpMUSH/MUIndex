---
kind: client
slug: atlantis
title: Atlantis
summary: 一个只跑在 macOS 上的客户端，活得久，在 beta 里也待得久。它的脚本功能有文档说明已经不能用了，这是本节里唯一一个诚实的“否”。
home: https://www.riverdark.net/atlantis/
platform: macOS
capability: screen reader | unknown |
capability: TLS | yes | https://www.riverdark.net/atlantis/history.php
capability: UTF-8 | yes | https://www.riverdark.net/atlantis/history.php
capability: MCCP | yes | https://www.riverdark.net/atlantis/history.php
capability: GMCP | unknown |
capability: MSDP | unknown |
capability: ATCP | unknown |
capability: MXP | unknown |
capability: MSP | unknown |
capability: scripting | no | https://www.riverdark.net/atlantis/
see-also: clients/mudlet
see-also: protocols/charset
---

Atlantis 是一个原生 macOS 客户端，从 Mac OS X 10.3 起就在了，并在 Catalina 时期更新到了 64 位。它
能处理 RFC 2066 字符集协商和 Unicode，这比它的年纪所暗示的要好，而且它支持 MCCP 和 SSL。

## 本节里唯一的那个“否”

它的脚本功能是 Perl，经由 CamelBones 桥接实现，而项目自己的主页说它已经不能用了——Apple 对 Perl 的
处理方式变了，而那个库的作者几年前去世了。这是一处*有出处的缺失*，与未知是不同的东西，也是整个客
户端部分里唯一带着这种答案的格子。别的每一处，诚实的答案都是我们无法确立。

## 我们无法确立的一切

它的版本历史完整而公开，其中提到了 **MCCP**、**SSL** 和**字符集协商**——而从未提到 GMCP、MSDP、
ATCP 或 MSP。MXP 出现过一次，作为打算放进 1.0.0 之后某个版本的东西，而那个版本还没有到来。

脚本 API 里有一个 Perl 的 `Atlantis::Speak()` 调用，很容易把它读成屏幕阅读器支持。它不是：它是一个
脚本化的文本转语音调用，而它所在的那套脚本系统，项目自己说是不能用的。VoiceOver、“accessible”和
“screen reader”在主页、下载页、完整版本历史和已归档的用户指南上都不曾出现。

当前可下载的是 0.9.9.8，名义上仍是 beta，而站上任何地方都没有公布发布日期。
