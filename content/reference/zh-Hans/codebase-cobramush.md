---
kind: codebase
slug: cobramush
title: CobraMUSH
summary: 一个 PennMUSH 分支，有自己的分部（division）与权限模型。部署量不大，仍在应答。
codebase: CobraMUSH
home: https://cobramush.org/
see-also: codebases/pennmush
see-also: codebases/rhostmush
---

CobraMUSH 从 PennMUSH 分支而来，加入了一套*分部*（division）模型——一个可以逐级下放权限的管理权层
级，取代了它上游所用的 wizard/royalty 那种扁平区分。想把管理权切成小块分出去、又不愿把全部权限一
并交出的游戏，就是它的用户群。

为 PennMUSH 写的 softcode 大体上能跑，差异恰好集中在这次分支所针对的那个领域。

## 从外面看是什么样

没有 MSSP，登录前的 `WHO` 可用，而在我们实测的那个游戏上完全没有协商任何 telnet 选项。最后这一点不
是批评：什么都不协商的服务器，也就不可能把协商弄错，而纯文本走一个普通套接字，是这个爱好里每一个
客户端都应付得来的东西。
