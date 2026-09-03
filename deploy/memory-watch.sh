#!/usr/bin/env bash
# What the box was doing in the minutes before it ran out of memory.
#
# On 2026-08-18 the web container went from its steady 350-450 MB to 3.3 GB and took the host with
# it. Nothing survived to say why: `sar` samples every ten minutes and records only totals, the
# container log holds about half an hour and is destroyed by the restart, and the whole burst fits
# inside one sar interval. This closes that gap — a five-second sample of every container, and a
# one-shot capture of everything perishable the moment one of them starts to climb.
#
# Reads cgroup files rather than calling `docker stats`, deliberately: the sampling has to keep
# working in exactly the conditions that stop the Docker CLI from answering.
#
# Installed by deploy/muindex-memory-watch.service.
set -uo pipefail

# **Traefik rather than Caddy, and web-2 named even though there should not be one.**
#
# This list was written when there was one web container and a Caddy in front of it, and both facts
# changed underneath it: `muindex-caddy-1` has not existed since Traefik replaced it, so that column
# was empty for weeks, and for a while `deploy.replicas: 2` meant half the site was never sampled at
# all — a climb on the second replica looked, from here, like a quiet day.
#
# The compose file is back to one replica (see the note above `deploy:` there: two of them are an
# allowance of 4 GB on a 3.73 GiB host, which is the failure `mem_limit` exists to prevent). web-2 is
# still named, deliberately. A container that does not exist costs an empty column; a second replica
# that appears — a rollback, a hand-run `docker compose up --scale`, an experiment — and is not
# sampled costs the thing this file exists for, and would do it silently. The asymmetry is the whole
# argument: over-naming is a blank cell, under-naming is a blind spot.
CONTAINERS=(muindex-web-1 muindex-web-2 muindex-postgres-1 muindex-traefik-1 muindex-i3-1)

# Both, for the same reason. `WATCH` named a single container because there used to be a single
# container, and a burst on any other one produced no capture at all. Space-separated, so an operator
# can still narrow it.
WATCH=${WATCH:-"muindex-web-1 muindex-web-2"}
THRESHOLD_MB=${THRESHOLD_MB:-1000}     # steady is 350-450, the cgroup limit is 2048
INTERVAL=${INTERVAL:-5}
LOGDIR=${LOGDIR:-/var/log/muindex}
KEEP_DAYS=${KEEP_DAYS:-14}

mkdir -p "$LOGDIR"

# The cgroup path for a container, or failure if it is not running. Resolved through `docker
# inspect` only when the cached path stops existing, which is what a restart looks like from here.
declare -A CGROUP
cgroup_of() {
  local name=$1 path=${CGROUP[$1]:-} id
  if [ -z "$path" ] || [ ! -r "$path/memory.current" ]; then
    id=$(docker inspect -f '{{.Id}}' "$name" 2>/dev/null) || return 1
    [ -n "$id" ] || return 1
    path="/sys/fs/cgroup/system.slice/docker-$id.scope"
    [ -r "$path/memory.current" ] || return 1
    CGROUP[$name]=$path
  fi
  printf '%s' "$path"
}

meminfo() { awk -v k="$1:" '$1 == k { print int($2 / 1024) }' /proc/meminfo; }

# Everything that stops existing once the process has been killed. Written once per episode, where
# an episode ends when memory falls back well under the threshold.
capture() {
  local who=$1 rss=$2 dir pid
  dir="$LOGDIR/burst-$(date -u +%Y%m%dT%H%M%SZ)-$who"
  mkdir -p "$dir"
  printf 'container=%s rss_mb=%s threshold_mb=%s at=%s\n' \
    "$who" "$rss" "$THRESHOLD_MB" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$dir/when.txt"

  # Managed heap or native allocation is the first fork in the road, and smaps_rollup is what tells
  # them apart without attaching a debugger.
  pid=$(docker inspect -f '{{.State.Pid}}' "$who" 2>/dev/null)
  if [ -n "${pid:-}" ] && [ "$pid" != "0" ]; then
    cat "/proc/$pid/status"       > "$dir/status.txt"       2>/dev/null
    cat "/proc/$pid/smaps_rollup" > "$dir/smaps_rollup.txt" 2>/dev/null
    # Sockets in the container's own network namespace. An allocation driven by a stranger who is
    # still connected leaves its peer address here and nowhere else.
    nsenter -t "$pid" -n ss -tan  > "$dir/sockets.txt"      2>/dev/null
    top -b -H -n 1 -p "$pid"      > "$dir/threads.txt"      2>/dev/null
  fi

  # Both replicas' logs, not just the one that climbed: they share a load balancer, so the request
  # that started this may have been answered by the other one.
  docker logs --tail 3000 muindex-web-1 > "$dir/web-1.log" 2>&1
  docker logs --tail 3000 muindex-web-2 > "$dir/web-2.log" 2>&1

  # Traefik's access log, on stdout rather than in a file — the Caddy path this used to read went
  # away with Caddy and had been capturing nothing since.
  docker logs --tail 5000 muindex-traefik-1 > "$dir/access.log" 2>&1
  docker ps > "$dir/ps.txt" 2>&1

  # What the database was asked for, which is the other end of the same request.
  docker exec muindex-postgres-1 psql -U muindex -d muindex -c \
    "SELECT pid, state, wait_event_type, now()-query_start AS ran_for, left(query, 400) AS query
       FROM pg_stat_activity WHERE state <> 'idle' ORDER BY query_start;" \
    > "$dir/pg_activity.txt" 2>&1

  cp /proc/meminfo "$dir/meminfo.txt" 2>/dev/null
  logger -t muindex-memory-watch "burst captured in $dir ($who at ${rss}MB)"
}

# A container's anonymous resident memory: heap, stacks and everything else the process actually
# allocated, with file-backed pages left out.
#
# This is here because a cgroup total cannot answer the question a slow climb asks. `memory.current`
# counts the page cache too, so a container whose figure rises over days may be leaking, may be
# holding a larger managed heap the GC has not been pressed into returning, or may simply have read
# a lot of files. Anonymous memory rising while the total's other half stays flat says it is the
# process; the two rising together says it is not.
anon_mb() {
  local pid
  pid=$(docker inspect -f '{{.State.Pid}}' "$1" 2>/dev/null) || return 1
  [ -n "$pid" ] && [ "$pid" != "0" ] || return 1
  awk '/^Anonymous:/ { print int($2 / 1024); found = 1 }
       END { exit !found }' "/proc/$pid/smaps_rollup" 2>/dev/null
}

header() {
  local h="at,mem_available_mb,cached_mb,swap_free_mb" c
  for c in "${CONTAINERS[@]}"; do h="$h,${c}_mb"; done
  for c in "${WATCH[@]}"; do h="$h,${c}_anon_mb"; done
  printf '%s\n' "$h"
}

# One episode flag per watched container rather than one for the pair: replica 1 sitting above the
# threshold must not suppress the capture that replica 2 climbing would otherwise produce.
read -r -a WATCH <<< "$WATCH"
declare -A CAPTURED
ticks=0

while true; do
  file="$LOGDIR/memory-$(date -u +%Y-%m-%d).csv"
  [ -s "$file" ] || header > "$file"

  line="$(date -u +%Y-%m-%dT%H:%M:%SZ),$(meminfo MemAvailable),$(meminfo Cached),$(meminfo SwapFree)"
  declare -A CURRENT_MB=()
  for c in "${CONTAINERS[@]}"; do
    value=""
    if path=$(cgroup_of "$c"); then
      current=$(cat "$path/memory.current" 2>/dev/null) && value=$(( current / 1048576 ))
    fi
    line="$line,$value"
    CURRENT_MB[$c]=${value:-0}
  done

  for c in "${WATCH[@]}"; do
    line="$line,$(anon_mb "$c" || true)"
  done
  printf '%s\n' "$line" >> "$file"

  for c in "${WATCH[@]}"; do
    watch_mb=${CURRENT_MB[$c]:-0}

    if [ "$watch_mb" -ge "$THRESHOLD_MB" ]; then
      [ "${CAPTURED[$c]:-0}" -eq 0 ] && { capture "$c" "$watch_mb"; CAPTURED[$c]=1; }
    elif [ "$watch_mb" -lt $(( THRESHOLD_MB * 3 / 4 )) ]; then
      CAPTURED[$c]=0    # back to normal; the next climb is a new episode
    fi
  done

  ticks=$(( ticks + 1 ))
  if [ $(( ticks % 720 )) -eq 0 ]; then      # roughly hourly at the default interval
    find "$LOGDIR" -maxdepth 1 -name 'memory-*.csv' -mtime "+$KEEP_DAYS" -delete 2>/dev/null
    find "$LOGDIR" -maxdepth 1 -name 'burst-*' -type d -mtime "+$KEEP_DAYS" -exec rm -rf {} + 2>/dev/null
  fi

  sleep "$INTERVAL"
done
