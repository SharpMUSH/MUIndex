---
kind: codebase
slug: rhostmush
title: RhostMUSH
summary: 一个以精细的权限模型和庞大的内置函数集著称的 MUSH 服务器。没有 MSSP；会回应登录前的 WHO。
codebase: RhostMUSH
home: https://github.com/RhostMUSH/trunk
see-also: codebases/pennmush
see-also: codebases/tinymux
see-also: codebases/cobramush
---

RhostMUSH 是四个广泛使用的 TinyMUSH 系服务器中的第四个，也是管理模型最为繁复的一个：它的权限与标记
系统比它的亲戚们细致得多，游戏选择它通常就是为了这个。

它的内置函数库很大，为 Rhost 写的 softcode 往往无法干净地移植到 PennMUSH 或 TinyMUX，除非把用到了
其他服务器所没有的函数的那些部分重写掉。

## 从外面看是什么样

没有 MSSP。登录前的 `WHO` 会给出一个计数。CHARSET 有协商。

这个组合——没有 MSSP，`WHO` 可用——正是 MUSH 家族的标志，也正是本站要去探测登录画面的原因。以我们自
己那次调查的证据来看，MSSP 家族和 `WHO` 家族几乎互不相交：28 个代码库通过 MSSP 发布人数，7 个通过
`WHO`，两者都发布的只有 2 个。
