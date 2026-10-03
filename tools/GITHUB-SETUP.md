# 怎么拿到"远端地址 + 凭据"（逐步操作）

> 目标：把本仓库推到 GitHub，让 GameCI 自动出 Unity APK。
> 全程在手机上用浏览器/App 完成，不需要电脑。

---

## 第 1 步：注册 GitHub 账号（约 3 分钟）

1. 手机浏览器打开 **https://github.com/signup**
2. 填邮箱 → 设密码 → 取用户名（例如 `yourname`）
3. 邮箱里点验证链接

已有账号就跳过。

---

## 第 2 步：建一个空仓库（约 1 分钟）

1. 打开 **https://github.com/new**
2. **Repository name** 填 `whisper`（或任意名）
3. **Private** 或 Public 都行（Private 更稳，GameCI 在私有仓库同样免费）
4. **不要**勾 "Add a README / .gitignore / license"（要空的，否则推送会冲突）
5. 点 **Create repository**

创建后页面会显示仓库地址，形如：

```
https://github.com/你的用户名/whisper.git     ← 这就是"远端地址"
```

---

## 第 3 步：生成访问令牌（凭据，约 2 分钟）

> 密码不能用于推送，必须用 **Personal Access Token（PAT）**。

1. 打开 **https://github.com/settings/tokens?type=beta**（Fine-grained token，推荐）
   - **Token name**：`whisper-phone`
   - **Expiration**：90 天（到期再生成即可）
   - **Repository access**：选 **Only select repositories** → 选刚建的 `whisper`
   - **Permissions** → Repository permissions → 找到 **Contents** → 设为 **Read and write**
2. 点 **Generate token**
3. **立刻复制**那串 `github_pat_...`（离开页面就再也看不到）

> 或者用经典令牌：**https://github.com/settings/tokens/new** → 勾 **repo** → 生成。

---

## 第 4 步：告诉我两样东西

把这两项发我（我会用它推送，不会打印出来）：

```
远端地址:  https://github.com/你的用户名/whisper.git
令牌:      github_pat_xxxxxxxx
```

**安全提醒**：令牌等于密码。建议按上面的"90 天 + 只给这一个仓库"设置；
推送完成后你也可以随时在 https://github.com/settings/tokens 一键撤销。

---

## 第 5 步：配置 Unity 许可证 Secrets（出包必需）

CI 里必须能激活 Unity。用 GameCI 官方流程拿许可证：

1. 手机浏览器打开 **https://game.ci/docs/unity/activation**
2. 按其说明用你的 Unity 账号（没有就注册 **https://id.unity.com**，个人版免费）
3. 在仓库页面 **Settings → Secrets and variables → Actions** 里新增 3 个 Secret：
   - `UNITY_LICENSE` —— 许可证文件（`.ulf`）的**全文内容**
   - `UNITY_EMAIL` —— Unity 账号邮箱
   - `UNITY_PASSWORD` —— Unity 账号密码

配好后在 **Actions** 页手动触发 `unity-android` 工作流，产物里就能下载 APK。

---

## 常见问题

**Q：一定要 GitHub 吗？**
A：不一定。Gitee / Codeberg / 自建 Git 也能推，但 **GameCI 的工作流只跑在 GitHub Actions 上**。
若你倾向国内托管，我改配 GitCode/Gitee 的构建方案（需要另配构建机，不如 GitHub 省事）。

**Q：令牌会不会泄露？**
A：我推送时用 `tools/git.sh` 包装脚本，凭据只经环境变量传给 git；
我不会把它写进任何文件或提交历史。你也可以用完立刻撤销。

**Q：能不能不给我令牌，我自己推？**
A：可以。我把仓库整理好、给你一条命令，你在手机上用任意 git 客户端（如 Termux、GitHub App）推即可。
