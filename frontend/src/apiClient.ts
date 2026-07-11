import { Configuration, DefaultApi } from "./api";
import { readAuth } from "./authSession";

export const apiClient = new DefaultApi(
  new Configuration({
    basePath: import.meta.env.VITE_API_BASE_URL ?? "",
    accessToken: () => readAuth()?.token ?? "",
  }),
);
