//! 启动时检查 GitHub Release 是否有新版本（后台请求，不阻塞 UI）。

use crate::shared_constants::update_check::{RELEASES_URL, REQUEST_TIMEOUT_MS};

/// 检测到有新版本时携带的信息
#[derive(Clone, Debug)]
pub struct UpdateInfo {
    /// 最新版本号（如 "0.2.0"），可用于展示或后续扩展
    #[allow(dead_code)]
    pub version: String,
    /// 该版本 Release 页面 URL
    pub url: String,
}

/// 更新检查结果：有更新 / 已是最新 / 检查失败
#[derive(Clone, Debug)]
pub enum UpdateCheckResult {
    /// 发现新版本
    UpdateAvailable(UpdateInfo),
    /// 已是最新，并已知远程最新版本号
    UpToDate { latest_version: String },
    /// 网络或解析失败，携带原因便于排查
    CheckFailed(String),
}

/// 在后台线程中调用：请求 GitHub API，返回三种结果之一（有更新 / 已是最新 / 检查失败）。
pub fn check_github_update() -> UpdateCheckResult {
    let current = current_version();
    let body = match request_releases() {
        Ok(b) => b,
        Err(reason) => return UpdateCheckResult::CheckFailed(reason),
    };
    let latest = match parse_latest_release(&body) {
        Some(l) => l,
        None => {
            return UpdateCheckResult::CheckFailed(
                "解析失败或仓库暂无 Release".to_string(),
            );
        }
    };
    let latest_ver = normalize_version(&latest.tag_name);
    if version_greater_than(&latest.tag_name, &current) {
        UpdateCheckResult::UpdateAvailable(UpdateInfo {
            version: latest_ver,
            url: latest.html_url,
        })
    } else {
        UpdateCheckResult::UpToDate {
            latest_version: latest_ver,
        }
    }
}

fn current_version() -> String {
    env!("CARGO_PKG_VERSION").to_string()
}

#[derive(serde::Deserialize)]
struct GhRelease {
    tag_name: String,
    html_url: String,
}

fn request_releases() -> Result<String, String> {
    let resp = ureq::get(RELEASES_URL)
        .set("Accept", "application/vnd.github.v3+json")
        .timeout(std::time::Duration::from_millis(REQUEST_TIMEOUT_MS))
        .call()
        .map_err(|e| {
            let msg = e.to_string();
            if msg.contains("Connection refused") || msg.contains("failed to connect") {
                "网络连接失败".to_string()
            } else if msg.contains("timed out") || msg.contains("Timeout") {
                "请求超时".to_string()
            } else {
                format!("请求失败: {}", msg)
            }
        })?;
    if resp.status() != 200 {
        return Err(format!("HTTP {}", resp.status()));
    }
    resp.into_string()
        .map_err(|e| format!("读取响应失败: {}", e))
}

fn parse_latest_release(json: &str) -> Option<GhRelease> {
    let list: Vec<GhRelease> = serde_json::from_str(json).ok()?;
    list.into_iter().next()
}

fn strip_leading_v(tag: &str) -> &str {
    let s = tag.trim();
    match s.as_bytes().first() {
        Some(b'v' | b'V') => &s[1..],
        _ => s,
    }
}

/// 规范化 tag（去掉 "v" 前缀），便于显示
fn normalize_version(tag: &str) -> String {
    strip_leading_v(tag).to_string()
}

/// 比较 remote 是否严格大于 current（按 semver 三段数字比较）
fn version_greater_than(remote_tag: &str, current: &str) -> bool {
    let r = parse_three_parts(remote_tag);
    let c = parse_three_parts(current);
    match (r, c) {
        (Some((rm, rn, rp)), Some((cm, cn, cp))) => {
            rm > cm || (rm == cm && rn > cn) || (rm == cm && rn == cn && rp > cp)
        }
        _ => false,
    }
}

fn parse_three_parts(s: &str) -> Option<(u32, u32, u32)> {
    let s = strip_leading_v(s);
    let parts: Vec<&str> = s.splitn(3, '.').collect();
    if parts.len() != 3 {
        return None;
    }
    let major = parts[0].trim().parse().ok()?;
    let minor = parts[1].trim().parse().ok()?;
    let patch = parts[2].trim().parse().ok()?;
    Some((major, minor, patch))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_version_greater_than() {
        assert!(version_greater_than("0.2.0", "0.1.0"));
        assert!(version_greater_than("1.0.0", "0.9.9"));
        assert!(version_greater_than("v0.2.0", "0.1.0"));
        assert!(!version_greater_than("0.1.0", "0.1.0"));
        assert!(!version_greater_than("0.1.0", "0.2.0"));
        assert!(!version_greater_than("v0.1.0", "0.2.0"));
    }

    #[test]
    fn test_parse_three_parts() {
        assert_eq!(parse_three_parts("0.1.0"), Some((0, 1, 0)));
        assert_eq!(parse_three_parts("2.3.4"), Some((2, 3, 4)));
        assert_eq!(parse_three_parts("v1.2.3"), Some((1, 2, 3)));
        assert_eq!(parse_three_parts("V0.0.1"), Some((0, 0, 1)));
    }

    #[test]
    fn test_normalize_version_strips_v() {
        assert_eq!(normalize_version("v1.2.3"), "1.2.3");
        assert_eq!(normalize_version("  V2.0.0  "), "2.0.0");
    }
}
