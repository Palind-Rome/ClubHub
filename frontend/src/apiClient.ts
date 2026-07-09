import { Configuration, DefaultApi } from "./api";

export const apiClient = new DefaultApi(new Configuration({ basePath: "" }));
