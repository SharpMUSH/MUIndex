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

CONTAINERS=(muindex-web-1 muindex-postgres-1 muindex-i3-1 muindex-caddy-1)
WATCH=${WATCH:-muindex-web-1}          # the one with the history
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
  local rss=$1 dir pid
  dir="$LOGDIR/burst-$(date -u +%Y%m%dT%H%M%SZ)"
  mkdir -p "$dir"
  printf 'container=%s rss_mb=%s threshold_mb=%s at=%s\n' \
    "$WATCH" "$rss" "$THRESHOLD_MB" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$dir/when.txt"

  # Managed heap or native allocation is the first fork in the road, and smaps_rollup is what tells
  # them apart without attaching a debugger.
  pid=$(docker inspect -f '{{.State.Pid}}' "$WATCH" 2>/dev/null)
  if [ -n "${pid:-}" ] && [ "$pid" != "0" ]; then
    cat "/proc/$pid/status"       > "$dir/status.txt"       2>/dev/null
    cat "/proc/$pid/smaps_rollup" > "$dir/smaps_rollup.txt" 2>/dev/null
    # Sockets in the container's own network namespace. An allocation driven by a stranger who is
    # still connected leaves its peer address here and nowhere else.
    nsenter -t "$pid" -n ss -tan  > "$dir/sockets.txt"      2>/dev/null
    top -b -H -n 1 -p "$pid"      > "$dir/threads.txt"      2>/dev/null
  fi

  docker logs --tail 3000 "$WATCH" > "$dir/web.log" 2>&1
  docker exec muindex-caddy-1 tail -n 5000 /var/log/caddy/access.log > "$dir/access.log" 2>/dev/null
  docker ps > "$dir/ps.txt" 2>&1

  # What the database was asked for, which is the other end of the same request.
  docker exec muindex-postgres-1 psql -U muindex -d muindex -c \
    "SELECT pid, state, wait_event_type, now()-query_start AS ran_for, left(query, 400) AS query
       FROM pg_stat_activity WHERE state <> 'idle' ORDER BY query_start;" \
    > "$dir/pg_activity.txt" 2>&1

  cp /proc/meminfo "$dir/meminfo.txt" 2>/dev/null
  logger -t muindex-memory-watch "burst captured in $dir ($WATCH at ${rss}MB)"
}

header() {
  local h="at,mem_available_mb,cached_mb,swap_free_mb" c
  for c in "${CONTAINERS[@]}"; do h="$h,${c}_mb"; done
  printf '%s\n' "$h"
}

captured=0
ticks=0

while true; do
  file="$LOGDIR/memory-$(date -u +%Y-%m-%d).csv"
  [ -s "$file" ] || header > "$file"

  line="$(date -u +%Y-%m-%dT%H:%M:%SZ),$(meminfo MemAvailable),$(meminfo Cached),$(meminfo SwapFree)"
  watch_mb=0
  for c in "${CONTAINERS[@]}"; do
    value=""
    if path=$(cgroup_of "$c"); then
      current=$(cat "$path/memory.current" 2>/dev/null) && value=$(( current / 1048576 ))
    fi
    line="$line,$value"
    [ "$c" = "$WATCH" ] && watch_mb=${value:-0}
  done
  printf '%s\n' "$line" >> "$file"

  if [ "$watch_mb" -ge "$THRESHOLD_MB" ]; then
    [ "$captured" -eq 0 ] && { capture "$watch_mb"; captured=1; }
  elif [ "$watch_mb" -lt $(( THRESHOLD_MB * 3 / 4 )) ]; then
    captured=0        # back to normal; the next climb is a new episode
  fi

  ticks=$(( ticks + 1 ))
  if [ $(( ticks % 720 )) -eq 0 ]; then      # roughly hourly at the default interval
    find "$LOGDIR" -maxdepth 1 -name 'memory-*.csv' -mtime "+$KEEP_DAYS" -delete 2>/dev/null
    find "$LOGDIR" -maxdepth 1 -name 'burst-*' -type d -mtime "+$KEEP_DAYS" -exec rm -rf {} + 2>/dev/null
  fi

  sleep "$INTERVAL"
done
