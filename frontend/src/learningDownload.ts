export interface PrepareLearningDownloadOptions {
  itemId: number;
  token: string;
  confirmDownload: () => Promise<unknown>;
  fetchFile?: typeof fetch;
  createObjectUrl?: (file: Blob) => string;
}

export async function prepareLearningDownload({
  itemId,
  token,
  confirmDownload,
  fetchFile = fetch,
  createObjectUrl = URL.createObjectURL,
}: PrepareLearningDownloadOptions): Promise<string> {
  const response = await fetchFile(`/api/v1/learning/items/${itemId}/file?download=true`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!response.ok) {
    let message = "文件内容获取失败";
    try {
      const payload = (await response.json()) as {
        message?: string;
        detail?: string;
        title?: string;
      };
      message = payload.detail || payload.message || payload.title || message;
    } catch {
      /* 非 JSON 错误响应沿用稳定的中文提示。 */
    }
    throw new Error(message);
  }

  const file = await response.blob();
  await confirmDownload();
  return createObjectUrl(file);
}
