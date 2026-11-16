import { json, mockApi } from "../test/http";
import { api, ifMatch, resourceOf } from "./client";

const id = "0b7e2f6a-3c1d-4e5f-8a9b-0c1d2e3f4a5b";

describe("resourceOf", () => {
  it("names the resource a path belongs to", () => {
    expect(resourceOf(`/api/work-orders/${id}`)).toBe(`/api/work-orders/${id}`);
    expect(resourceOf(`/api/work-orders/${id}/approve`)).toBe(`/api/work-orders/${id}`);
    expect(resourceOf(`/api/customers/${id}/vehicles`)).toBe(`/api/customers/${id}`);
    expect(resourceOf("/api/customers")).toBeNull();
    expect(resourceOf("/api/schedule")).toBeNull();
  });
});

describe("ifMatch", () => {
  it("returns the last version the API sent for a resource, from reads and changes alike", async () => {
    mockApi((request) =>
      request.method === "GET" ? json({}, 200, { ETag: '"41"' }) : json({}, 200, { ETag: '"42"' }),
    );

    expect(ifMatch(`/api/bays/${id}`)).toEqual({});

    await api.GET("/api/bays/{id}", { params: { path: { id } } });
    expect(ifMatch(`/api/bays/${id}`)).toEqual({ "If-Match": '"41"' });

    await api.POST("/api/bays/{id}/take-out-of-service", { params: { path: { id } } });
    expect(ifMatch(`/api/bays/${id}`)).toEqual({ "If-Match": '"42"' });
  });

  it("ignores the tags of failed answers", async () => {
    const other = "1b7e2f6a-3c1d-4e5f-8a9b-0c1d2e3f4a5b";
    mockApi(() => json({ status: 404, code: "bay.not_found" }, 404, { ETag: '"9"' }));

    await api.GET("/api/bays/{id}", { params: { path: { id: other } } });

    expect(ifMatch(`/api/bays/${other}`)).toEqual({});
  });
});
