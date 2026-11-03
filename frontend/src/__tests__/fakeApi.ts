/**
 * In-memory HTTP fake for the shared axios instance (`services/api`). It replaces the
 * adapter, so the real request/response interceptors still run, but nothing touches the
 * network. Register routes with `on(method, url, reply)`, then assert on `requests`.
 *
 *   const http = installFakeApi();
 *   http.on('get', '/admin/users', () => ({ status: 200, data: buildAdminUserList() }));
 *   ...
 *   expect(http.requests[0]).toMatchObject({ method: 'get', url: '/admin/users' });
 *   afterEach(() => http.restore());
 */
import type { AxiosAdapter, AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import api from '../services/api';

export type HttpMethod = 'get' | 'post' | 'put' | 'delete' | 'patch';

export interface RecordedRequest {
  method: HttpMethod;
  url: string;
  params: Record<string, unknown> | undefined;
  /** The parsed JSON body, or undefined when the request had none. */
  body: unknown;
  headers: Record<string, unknown>;
}

export interface FakeReply {
  status?: number;
  data?: unknown;
}

export type ReplyFn = (req: RecordedRequest) => FakeReply | Promise<FakeReply>;

export interface FakeApi {
  requests: RecordedRequest[];
  /** Route `method url` (exact path, or a RegExp) to a reply. Later routes win. */
  on(method: HttpMethod, url: string | RegExp, reply: ReplyFn | FakeReply): FakeApi;
  restore(): void;
}

function parseBody(data: unknown): unknown {
  if (typeof data !== 'string' || data === '') return data ?? undefined;
  try {
    return JSON.parse(data);
  } catch {
    return data;
  }
}

export function installFakeApi(): FakeApi {
  const previous = api.defaults.adapter;
  const routes: Array<{ method: HttpMethod; url: string | RegExp; reply: ReplyFn }> = [];
  const requests: RecordedRequest[] = [];

  const adapter: AxiosAdapter = async (config: InternalAxiosRequestConfig) => {
    const req: RecordedRequest = {
      method: (config.method ?? 'get').toLowerCase() as HttpMethod,
      url: config.url ?? '',
      params: config.params as Record<string, unknown> | undefined,
      body: parseBody(config.data),
      headers: { ...(config.headers as Record<string, unknown>) },
    };
    requests.push(req);

    const route = [...routes]
      .reverse()
      .find((r) => r.method === req.method && (typeof r.url === 'string' ? r.url === req.url : r.url.test(req.url)));
    const { status = 200, data = null } = route
      ? await route.reply(req)
      : { status: 404, data: { detail: `fakeApi: no route for ${req.method.toUpperCase()} ${req.url}` } };

    const response: AxiosResponse = { data, status, statusText: String(status), headers: {}, config };
    if (status >= 200 && status < 300) return response;
    return Promise.reject({
      isAxiosError: true,
      message: `Request failed with status code ${status}`,
      config,
      response,
    });
  };

  api.defaults.adapter = adapter;

  const fake: FakeApi = {
    requests,
    on(method, url, reply) {
      routes.push({ method, url, reply: typeof reply === 'function' ? reply : () => reply });
      return fake;
    },
    restore() {
      api.defaults.adapter = previous;
    },
  };
  return fake;
}
