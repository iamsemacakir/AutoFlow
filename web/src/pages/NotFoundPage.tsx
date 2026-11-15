import { Link } from "react-router-dom";
import { PageHeader } from "../components/ui";

export function NotFoundPage() {
  return (
    <>
      <PageHeader title="Page not found" />
      <p>
        <Link to="/">Back to the schedule</Link>
      </p>
    </>
  );
}
