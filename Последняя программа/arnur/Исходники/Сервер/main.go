package main

// =====================================================================
//  ПРОМЕЖУТОЧНЫЙ СЕРВЕР СВЯЗИ ДИСПЕТЧЕРОВ (АКЖТ ↔ Алгабас)
//
//  Запускается на любом ПК в сети. Программы станций подключаются к нему
//  по WebSocket (ws://<адрес>:8090/ws), присылают показания своих светофоров,
//  сервер пересылает их другой станции.
//
//  Запуск:  PromServer.exe              (порт 8090)
//           PromServer.exe -addr :9000  (другой порт)
//           PromServer.exe -v           (писать в журнал каждое изменение показаний)
//  Страница состояния: http://<адрес>:8090/  (кто подключён, какие показания)
// =====================================================================

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"html/template"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"time"
)

const ServerVersion = "PromServer 1.4"

var (
	addr    = flag.String("addr", ":8090", "адрес и порт сервера")
	verbose = flag.Bool("v", false, "писать в журнал каждое изменение показаний")
)

func main() {
	flag.Parse()
	log.SetFlags(log.Ldate | log.Ltime)

	hub := NewHub()
	mux := http.NewServeMux()
	mux.HandleFunc("/ws", func(w http.ResponseWriter, r *http.Request) { ServeWS(hub, w, r) })
	mux.HandleFunc("/status", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json; charset=utf-8")
		json.NewEncoder(w).Encode(hub.Snapshot())
	})
	mux.HandleFunc("/{$}", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		if err := statusPage.Execute(w, hub.Snapshot()); err != nil {
			log.Printf("страница состояния: %v", err)
		}
	})

	ln, err := net.Listen("tcp", *addr)
	if err != nil {
		fatalf("Не удалось открыть порт %s: %v\n(порт занят другой программой? запустите с -addr :9000)", *addr, err)
	}
	srv := &http.Server{Handler: mux, ReadHeaderTimeout: 10 * time.Second}

	printBanner(ln.Addr().(*net.TCPAddr).Port)

	go func() {
		if err := srv.Serve(ln); err != nil && err != http.ErrServerClosed {
			fatalf("Сервер остановлен с ошибкой: %v", err)
		}
	}()

	stop := make(chan os.Signal, 1)
	signal.Notify(stop, os.Interrupt)
	<-stop
	log.Println("Остановка сервера...")
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	srv.Shutdown(ctx)
}

// printBanner — что вписать в программы станций: адреса этого ПК в локальной сети.
func printBanner(port int) {
	fmt.Println("==============================================================")
	fmt.Println(" " + ServerVersion + " — сервер связи диспетчеров АКЖТ <-> Алгабас")
	fmt.Println("==============================================================")
	ips := localIPv4()
	if len(ips) == 0 {
		fmt.Println(" Сетевых адресов не найдено — работает только на этом ПК:")
		ips = []string{"127.0.0.1"}
	} else {
		fmt.Println(" В программах станций укажите адрес сервера:")
	}
	for _, ip := range ips {
		fmt.Printf("     ws://%s:%d/ws\n", ip, port)
	}
	fmt.Println(" Состояние связи в браузере:")
	for _, ip := range ips {
		fmt.Printf("     http://%s:%d/\n", ip, port)
	}
	fmt.Println(" Остановить сервер — Ctrl+C или закрыть окно.")
	fmt.Println("==============================================================")
}

func localIPv4() []string {
	var out []string
	ifaces, err := net.Interfaces()
	if err != nil {
		return nil
	}
	for _, ifc := range ifaces {
		if ifc.Flags&net.FlagUp == 0 || ifc.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, _ := ifc.Addrs()
		for _, a := range addrs {
			ipn, ok := a.(*net.IPNet)
			if !ok || ipn.IP.To4() == nil || ipn.IP.IsLinkLocalUnicast() {
				continue
			}
			out = append(out, ipn.IP.String())
		}
	}
	return out
}

var statusPage = template.Must(template.New("status").Funcs(template.FuncMap{
	"lines": formatLines,
	"secs":  formatSections,
	"sigs": func(m map[string]string) string {
		if len(m) == 0 {
			return "—"
		}
		return strings.ReplaceAll(formatSignals(m), ", ", "   ")
	},
}).Parse(`<!doctype html>
<html lang="ru"><head><meta charset="utf-8"><meta http-equiv="refresh" content="2">
<title>Связь диспетчеров</title>
<style>
 body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#f6f7f9;color:#222}
 table{border-collapse:collapse;background:#fff}
 th,td{border:1px solid #ccc;padding:6px 12px;text-align:left}
 th{background:#e9ecef}
 .on{color:#087f23;font-weight:bold} .off{color:#b00020;font-weight:bold}
 small{color:#666}
</style></head><body>
<h2>Связь диспетчеров АКЖТ ↔ Алгабас</h2>
{{if .}}
<table>
<tr><th>Станция</th><th>Связь</th><th>Адрес</th><th>С</th><th>Показания светофоров</th><th>Направление перегонов</th><th>Занятость общих участков</th><th>Изменены</th></tr>
{{range .}}
<tr><td>{{.Name}} <small>({{.Station}})</small></td>
<td>{{if .Online}}<span class="on">есть</span>{{else}}<span class="off">нет</span>{{end}}</td>
<td>{{.Addr}}</td><td>{{.Since}}</td><td>{{sigs .Signals}}</td><td>{{lines .Lines}}</td><td>{{secs .Sections}}</td><td>{{.Updated}}</td></tr>
{{end}}
</table>
{{else}}<p>Ни одна станция ещё не подключалась.</p>{{end}}
<p><small>Страница обновляется каждые 2 с.</small></p>
</body></html>`))

// fatalf — ошибка, после которой сервер работать не может. Окно не закрывается сразу,
// иначе при запуске двойным щелчком сообщение никто не успеет прочитать.
func fatalf(format string, args ...any) {
	log.Printf(format, args...)
	fmt.Println("Нажмите Enter, чтобы закрыть окно.")
	fmt.Scanln()
	os.Exit(1)
}
