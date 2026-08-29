import { describe, expect, it, vi } from "vitest";
import { prepareLearningDownload } from "./learningDownload";

describe("prepareLearningDownload", () => {
  it("does not confirm a download when the protected file request fails", async () => {
    const confirmDownload = vi.fn();
    const fetchFile = vi.fn().mockResolvedValue({ ok: false });

    await expect(
      prepareLearningDownload({
        itemId: 12,
        token: "token",
        confirmDownload,
        fetchFile: fetchFile as unknown as typeof fetch,
      }),
    ).rejects.toThrow("文件内容获取失败");

    expect(confirmDownload).not.toHaveBeenCalled();
  });

  it("surfaces the API problem detail when private storage is unavailable", async () => {
    const confirmDownload = vi.fn();
    const fetchFile = vi.fn().mockResolvedValue({
      ok: false,
      json: vi.fn().mockResolvedValue({ detail: "无法从私有 OSS 读取文件，请稍后重试。" }),
    });

    await expect(
      prepareLearningDownload({
        itemId: 12,
        token: "token",
        confirmDownload,
        fetchFile: fetchFile as unknown as typeof fetch,
      }),
    ).rejects.toThrow("无法从私有 OSS 读取文件，请稍后重试。");

    expect(confirmDownload).not.toHaveBeenCalled();
  });

  it("does not confirm a download when reading the response body fails", async () => {
    const confirmDownload = vi.fn();
    const fetchFile = vi.fn().mockResolvedValue({
      ok: true,
      blob: vi.fn().mockRejectedValue(new Error("stream interrupted")),
    });

    await expect(
      prepareLearningDownload({
        itemId: 12,
        token: "token",
        confirmDownload,
        fetchFile: fetchFile as unknown as typeof fetch,
      }),
    ).rejects.toThrow("stream interrupted");

    expect(confirmDownload).not.toHaveBeenCalled();
  });

  it("confirms only after the complete response body has been read", async () => {
    const events: string[] = [];
    const file = new Blob(["resource"]);
    const confirmDownload = vi.fn(async () => {
      events.push("confirm");
    });
    const fetchFile = vi.fn().mockResolvedValue({
      ok: true,
      blob: async () => {
        events.push("body");
        return file;
      },
    });
    const createObjectUrl = vi.fn(() => {
      events.push("object-url");
      return "blob:resource";
    });

    await expect(
      prepareLearningDownload({
        itemId: 12,
        token: "token",
        confirmDownload,
        fetchFile: fetchFile as unknown as typeof fetch,
        createObjectUrl,
      }),
    ).resolves.toBe("blob:resource");

    expect(events).toEqual(["body", "confirm", "object-url"]);
  });
});
