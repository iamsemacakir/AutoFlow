import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, ifMatch, isStale, unwrap } from "../api/client";
import type { BayDetails } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Loading, PageHeader } from "../components/ui";

export function BaysPage() {
  const { hasRole } = useAuth();
  const owner = hasRole("Owner");
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const bays = useQuery({ queryKey: ["bays"], queryFn: async () => unwrap(await api.GET("/api/bays")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["bays"] });

  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST("/api/bays", { body: { name } })),
    onSuccess: async () => {
      setName("");
      await refresh();
    },
  });

  return (
    <>
      <PageHeader title="Bays" subtitle="The workshop's capacity: one vehicle per bay at a time." />
      {owner && (
        <form
          className="toolbar"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <input required placeholder="Name of the new bay, e.g. Lift 3" value={name} onChange={(e) => setName(e.target.value)} aria-label="Bay name" />
          <button type="submit" className="primary" disabled={create.isPending}>
            Add bay
          </button>
        </form>
      )}
      <ErrorMessage error={create.error ?? bays.error} />
      {bays.isPending && <Loading />}
      {bays.data && (
        <Card>
          {bays.data.length === 0 ? (
            <Empty>No bays yet.</Empty>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Bay</th>
                  <th>State</th>
                  {owner && <th />}
                </tr>
              </thead>
              <tbody>
                {bays.data.map((bay) => (
                  <BayRow key={bay.id} bay={bay} owner={owner} onChanged={refresh} />
                ))}
              </tbody>
            </table>
          )}
        </Card>
      )}
    </>
  );
}

/**
 * One bay with the owner's actions. The list carries no versions (ADR 0024),
 * so for an owner the row loads its bay: that shows its current name and
 * state and gives the version that renaming and taking it out of or back
 * into service send as `If-Match`. Until it has arrived, the actions wait.
 */
function BayRow({ bay: listed, owner, onChanged }: { bay: BayDetails; owner: boolean; onChanged: () => Promise<unknown> }) {
  const queryClient = useQueryClient();
  const current = useQuery({
    queryKey: ["bay", listed.id],
    queryFn: async () => unwrap(await api.GET("/api/bays/{id}", { params: { path: { id: listed.id } } })),
    enabled: owner,
    placeholderData: listed,
  });
  const bay = current.data ?? listed;
  const loaded = !current.isPlaceholderData && current.isSuccess;
  const [name, setName] = useState(listed.name);
  const path = { params: { path: { id: bay.id } } };
  const headers = () => ifMatch(`/api/bays/${bay.id}`);

  const changed = async (next: BayDetails) => {
    queryClient.setQueryData(["bay", bay.id], next);
    await onChanged();
  };
  // A colleague changed the bay since the page was loaded: show it as it is now;
  // a typed name stays in the field.
  const reloadIfStale = (error: unknown) =>
    isStale(error)
      ? Promise.all([queryClient.invalidateQueries({ queryKey: ["bay", bay.id] }), onChanged()])
      : undefined;

  const rename = useMutation({
    mutationFn: async () => unwrap(await api.PUT("/api/bays/{id}", { ...path, body: { name }, headers: headers() })),
    onSuccess: changed,
    onError: reloadIfStale,
  });
  const toggle = useMutation({
    mutationFn: async () =>
      unwrap(
        bay.isInService
          ? await api.POST("/api/bays/{id}/take-out-of-service", { ...path, headers: headers() })
          : await api.POST("/api/bays/{id}/return-to-service", { ...path, headers: headers() }),
      ),
    onSuccess: changed,
    onError: reloadIfStale,
  });

  return (
    <tr>
      <td>
        {owner ? (
          <form
            className="inline-form"
            onSubmit={(e) => {
              e.preventDefault();
              rename.mutate();
            }}
          >
            <input value={name} onChange={(e) => setName(e.target.value)} aria-label={`Name of ${bay.name}`} />
            {name !== bay.name && (
              <button type="submit" disabled={!loaded || rename.isPending}>
                Rename
              </button>
            )}
          </form>
        ) : (
          bay.name
        )}
        <ErrorMessage error={rename.error ?? toggle.error ?? current.error} />
      </td>
      <td>{bay.isInService ? "In service" : <span className="warning">Out of service</span>}</td>
      {owner && (
        <td className="number">
          <button type="button" onClick={() => toggle.mutate()} disabled={!loaded || toggle.isPending}>
            {bay.isInService ? "Take out of service" : "Return to service"}
          </button>
        </td>
      )}
    </tr>
  );
}
