import { describe, expect, it, vi } from "vitest";
import { prepareLearningPreview } from "./learningPreview";

describe("prepareLearningPreview", () => {
  it("surfaces the structured detail returned when preview setup fails", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          code: "SERVICE_UNAVAILABLE",
          message: "资源预览暂不可用",
          detail: "无法从私有 OSS 读取预览副本。",
        }),
        { status: 503, headers: { "Content-Type": "application/json" } },
      ),
    );

    await expect(
      prepareLearningPreview(10, "token", fetchMock as unknown as typeof fetch),
    ).rejects.toThrow("无法从私有 OSS 读取预览副本。");
  });

  it("returns a PDF object URL as soon as the complete Blob has been read", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(null, {
          status: 204,
          headers: { "X-ClubHub-Preview-Kind": "pdf" },
        }),
      )
      .mockResolvedValueOnce(
        new Response("pdf", { status: 200, headers: { "Content-Type": "application/pdf" } }),
      );
    const createObjectUrl = vi.fn().mockReturnValue("blob:clubhub-preview");

    const result = await prepareLearningPreview(
      10,
      "token",
      fetchMock as unknown as typeof fetch,
      createObjectUrl,
    );

    expect(result).toEqual({
      kind: "pdf",
      converted: false,
      url: "blob:clubhub-preview",
      objectUrl: true,
    });
    expect(createObjectUrl).toHaveBeenCalledOnce();
    expect(createObjectUrl.mock.calls[0]?.[0].type).toBe("application/pdf");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});
