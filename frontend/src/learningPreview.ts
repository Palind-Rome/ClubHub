export type LearningPreviewResult = {
  kind: "image" | "video" | "pdf";
  converted: boolean;
  url: string;
  objectUrl: boolean;
};

async function readApiError(response: Response, fallback: string) {
  try {
    const payload = (await response.json()) as {
      message?: string;
      detail?: string;
      title?: string;
    };
    return payload.detail || payload.message || payload.title || fallback;
  } catch {
    return fallback;
  }
}

export async function prepareLearningPreview(
  itemId: number,
  token: string,
  fetchImpl: typeof fetch = fetch,
  createObjectUrl: (blob: Blob) => string = URL.createObjectURL,
): Promise<LearningPreviewResult> {
  const sessionResponse = await fetchImpl(`/api/v1/learning/items/${itemId}/preview-session`, {
    method: "POST",
    credentials: "same-origin",
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!sessionResponse.ok) {
    const fallback = `在线预览准备失败：HTTP ${sessionResponse.status}`;
    throw new Error(await readApiError(sessionResponse, fallback));
  }

  const headerKind = sessionResponse.headers.get("X-ClubHub-Preview-Kind");
  const kind = headerKind === "image" || headerKind === "video" ? headerKind : "pdf";
  const converted = sessionResponse.headers.get("X-ClubHub-Preview-Converted") === "true";
  const contentUrl = `/api/v1/learning/items/${itemId}/preview?v=${Date.now()}`;
  if (kind !== "pdf") return { kind, converted, url: contentUrl, objectUrl: false };

  const previewResponse = await fetchImpl(contentUrl, { credentials: "same-origin" });
  if (!previewResponse.ok) {
    throw new Error(`预览内容加载失败：HTTP ${previewResponse.status}`);
  }

  const previewBlob = await previewResponse.blob();
  if (previewBlob.type && previewBlob.type !== "application/pdf") {
    throw new Error("预览服务返回了非 PDF 内容");
  }
  return { kind, converted, url: createObjectUrl(previewBlob), objectUrl: true };
}
